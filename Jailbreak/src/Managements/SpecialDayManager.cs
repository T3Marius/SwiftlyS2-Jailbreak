using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class SpecialDayManager
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly ILogger<SpecialDayManager> _log;
    private readonly SpecialDayConfig _config;
    private readonly JBStatsDB _statsDB;
    private readonly Dictionary<string, ISpecialDay> _specialDays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, string> _participants = [];
    private readonly HashSet<ulong> _frozenPlayerKeys = [];
    private readonly IConVar<bool>? _teammatesAreEnemies;

    private Guid? _roundStartHookId;
    private Guid? _roundEndHookId;
    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _activeHudCts;
    private bool _currentDayStarted;
    private bool _countdownFreezeActive;
    private bool _friendlyFireEnabledBySpecialDay;

    public SpecialDayManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        IOptions<SpecialDayConfig> config,
        JBStatsDB statsDB,
        ILogger<SpecialDayManager> log)
    {
        _core = core;
        _players = players;
        _config = config.Value;
        _statsDB = statsDB;
        _log = log;
        _teammatesAreEnemies = _core.ConVar.Find<bool>("mp_teammates_are_enemies");
    }

    public IReadOnlyCollection<ISpecialDay> SpecialDays => _specialDays.Values.ToArray();
    public ISpecialDay? CurrentSpecialDay { get; private set; }
    public ISpecialDay? QueuedSpecialDay { get; private set; }
    public int CooldownRoundsRemaining { get; private set; }
    public bool IsSpecialDayActive => CurrentSpecialDay != null;
    public bool HasQueuedOrActiveSpecialDay => QueuedSpecialDay != null || CurrentSpecialDay != null;

    public void Register()
    {
        _roundStartHookId = _core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        _roundEndHookId = _core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        _core.Event.OnItemServicesCanAcquireHook += OnItemServicesCanAcquire;
        _core.Event.OnMapUnload += OnMapUnload;

        foreach (var command in _config.GunsCommands)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, SpecialGunsCommand);
        }
    }

    public void Unregister()
    {
        Unhook(ref _roundStartHookId);
        Unhook(ref _roundEndHookId);
        _core.Event.OnItemServicesCanAcquireHook -= OnItemServicesCanAcquire;
        _core.Event.OnMapUnload -= OnMapUnload;

        foreach (var command in _config.GunsCommands)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }

        CancelSpecialDay("manager unregister");
        QueuedSpecialDay = null;
        _specialDays.Clear();
    }

    public bool RegisterSpecialDay(ISpecialDay specialDay)
    {
        if (string.IsNullOrWhiteSpace(specialDay.Id))
            throw new ArgumentException("Special day id cannot be empty.", nameof(specialDay));

        if (!_specialDays.TryAdd(specialDay.Id, specialDay))
        {
            _log.LogWarning("Special day registration skipped because id is already registered. Id={Id}", specialDay.Id);
            return false;
        }

        _log.LogInformation("Registered special day. Id={Id}, Name={Name}", specialDay.Id, specialDay.Name);
        return true;
    }

    public bool UnregisterSpecialDay(string id)
    {
        if (!_specialDays.TryGetValue(id, out var specialDay))
            return false;

        if (ReferenceEquals(CurrentSpecialDay, specialDay))
            EndSpecialDay();

        _specialDays.Remove(id);
        _log.LogInformation("Unregistered special day. Id={Id}, Name={Name}", specialDay.Id, specialDay.Name);
        return true;
    }

    public bool QueueSpecialDay(string id)
    {
        if (!_specialDays.TryGetValue(id, out var specialDay))
            return false;

        if (QueuedSpecialDay != null || CooldownRoundsRemaining > 0 || !specialDay.CanStart())
            return false;

        QueuedSpecialDay = specialDay;
        _players.SendMessage(MessageType.Chat, "special_day_queued", true, args: specialDay.Name);
        _log.LogInformation("Queued special day for next round. Id={Id}, Name={Name}", specialDay.Id, specialDay.Name);
        return true;
    }

    public bool StartSpecialDay(string id)
    {
        if (!_specialDays.TryGetValue(id, out var specialDay))
            return false;

        if (CurrentSpecialDay != null)
            EndSpecialDay();

        if (!specialDay.CanStart())
        {
            _log.LogInformation("Special day refused to start. Id={Id}, Name={Name}", specialDay.Id, specialDay.Name);
            return false;
        }

        BeginSpecialDay(specialDay);
        return true;
    }

    public void EndSpecialDay()
    {
        FinishSpecialDay(announceAndRecord: true, reason: "round end");
    }

    private void CancelSpecialDay(string reason)
    {
        FinishSpecialDay(announceAndRecord: false, reason: reason);
    }

    private void FinishSpecialDay(bool announceAndRecord, string reason)
    {
        StopCountdown();
        StopActiveHud();
        UnfreezePlayers();

        var specialDay = CurrentSpecialDay;
        if (specialDay == null)
            return;

        CurrentSpecialDay = null;
        if (_currentDayStarted)
        {
            specialDay.End();
            if (announceAndRecord)
            {
                var winners = RecordSpecialDayStats();
                AnnounceSpecialDayEnded(specialDay, winners);
            }
        }

        RestoreSpecialDayConvars();
        _participants.Clear();
        _currentDayStarted = false;
        _log.LogInformation(
            "{Action} special day. Id={Id}, Name={Name}, Reason={Reason}",
            announceAndRecord ? "Ended" : "Cancelled",
            specialDay.Id,
            specialDay.Name,
            reason);
    }

    private void OnItemServicesCanAcquire(IOnItemServicesCanAcquireHookEvent e)
    {
        var specialDay = CurrentSpecialDay;
        if (specialDay == null || specialDay.AllowAllWeapons)
            return;

        var itemDefinitionIndex = (ItemDefinitionIndex)e.EconItemView.ItemDefinitionIndex;
        if (specialDay.AllowedWeapons.Contains(itemDefinitionIndex))
            return;

        e.SetAcquireResult(AcquireResult.NotAllowedByProhibition);
    }

    private void SpecialGunsCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        var specialDay = CurrentSpecialDay;
        if (specialDay == null || (!_currentDayStarted && !_countdownFreezeActive))
        {
            player.SendMessage(MessageType.Chat, "special_guns_no_active_day", true);
            return;
        }

        if (!specialDay.EnableGunsMenu)
        {
            player.SendMessage(MessageType.Chat, "special_guns_disabled", true);
            return;
        }

        var primaryWeapons = GetMenuWeapons(specialDay, SpecialDayWeapons.PrimaryWeapons).ToList();
        var secondaryWeapons = GetMenuWeapons(specialDay, SpecialDayWeapons.SecondaryWeapons).ToList();

        if (primaryWeapons.Count == 0 || secondaryWeapons.Count == 0)
        {
            player.SendMessage(MessageType.Chat, "special_guns_empty", true);
            return;
        }

        _core.MenusAPI.OpenMenuForPlayer(player.Player, PrimaryGunsMenu(player, primaryWeapons, secondaryWeapons));
    }

    private HookResult OnRoundStart(EventRoundStart e)
    {
        var queuedDay = QueuedSpecialDay;
        QueuedSpecialDay = null;

        if (CurrentSpecialDay != null)
            CancelSpecialDay("new round started while special day was active");

        if (queuedDay != null)
        {
            CooldownRoundsRemaining = Math.Max(0, _config.CooldownRounds);
            BeginSpecialDay(queuedDay);
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd e)
    {
        EndSpecialDay();

        if (CooldownRoundsRemaining > 0)
            CooldownRoundsRemaining--;

        return HookResult.Continue;
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        CancelSpecialDay("map unload");
        QueuedSpecialDay = null;
        CooldownRoundsRemaining = 0;
    }

    private void BeginSpecialDay(ISpecialDay specialDay)
    {
        CurrentSpecialDay = specialDay;
        _currentDayStarted = false;

        if (specialDay.StartCountdown <= 0)
        {
            StartCurrentSpecialDay(specialDay);
            return;
        }

        specialDay.PreStart();
        _countdownFreezeActive = true;
        FreezePlayers(specialDay, new Color(80, 170, 255, 255));
        var remaining = specialDay.StartCountdown;

        StopCountdown();
        _countdownCts = _core.Scheduler.RepeatBySeconds(1f, () =>
        {
            if (CurrentSpecialDay != specialDay)
            {
                StopCountdown();
                return;
            }

            if (remaining <= 0)
            {
                StopCountdown();
                UnfreezePlayers();
                StartCurrentSpecialDay(specialDay);
                return;
            }

            specialDay.OnCountdownTick(remaining);
            SendCountdownMessage(specialDay, remaining);
            remaining--;
        });
    }

    private void StartCurrentSpecialDay(ISpecialDay specialDay)
    {
        _countdownFreezeActive = false;
        CaptureParticipants();
        ApplySpecialDayConvars(specialDay);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            ApplyStartLoadout(specialDay);
            specialDay.Start();
            _currentDayStarted = true;
            StartActiveHud(specialDay);
            _log.LogInformation("Started special day. Id={Id}, Name={Name}", specialDay.Id, specialDay.Name);
        });
    }

    private void ApplySpecialDayConvars(ISpecialDay specialDay)
    {
        if (!specialDay.AllowFriendlyFire)
            return;

        _teammatesAreEnemies?.SetInternal(true);
        _core.Engine.ExecuteCommand("sv_teamid_overhead 0");
        _friendlyFireEnabledBySpecialDay = true;
    }

    private void RestoreSpecialDayConvars()
    {
        if (!_friendlyFireEnabledBySpecialDay)
            return;

        _teammatesAreEnemies?.SetInternal(false);
        _core.Engine.ExecuteCommand("sv_teamid_overhead 1");
        _friendlyFireEnabledBySpecialDay = false;
    }

    private void AnnounceSpecialDayEnded(ISpecialDay specialDay, IReadOnlyList<string> winners)
    {
        var winnerLabel = winners.Count == 1
            ? _core.Localizer["special_day_winner_label"]
            : _core.Localizer["special_day_winners_label"];
        var winnerText = winners.Count == 0
            ? "0"
            : string.Join("[silver], ", winners);

        _players.SendMessage(MessageType.Chat, "special_day_ended", true, args: [specialDay.Name, winnerLabel, winnerText]);
    }

    private void CaptureParticipants()
    {
        _participants.Clear();

        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid || !player.Player.IsAlive)
                continue;

            _participants[PlayerIdentity.GetKey(player.Player)] = player.Player.Name;
        }
    }

    private IReadOnlyList<string> RecordSpecialDayStats()
    {
        if (_participants.Count == 0)
            return [];

        var aliveParticipants = new Dictionary<ulong, IJBPlayer>();
        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid || !player.Player.IsAlive)
                continue;

            var playerKey = PlayerIdentity.GetKey(player.Player);
            if (_participants.ContainsKey(playerKey))
                aliveParticipants[playerKey] = player;
        }
        var winners = new List<string>();

        foreach (var (playerKey, playerName) in _participants)
        {
            if (aliveParticipants.TryGetValue(playerKey, out var player))
            {
                var stats = _statsDB.AddSpecialDayWin(playerKey, player.Player.Name);
                winners.Add(FormatWinnerName(player, stats.SpecialDayWins));
            }
            else
            {
                _statsDB.AddSpecialDayLoss(playerKey, playerName);
            }
        }

        return winners;
    }

    private static string FormatWinnerName(IJBPlayer player, int wins)
    {
        var color = player.Team switch
        {
            JBTeam.Guard => "[blue]",
            JBTeam.Prisoner => "[gold]",
            _ => "[silver]"
        };

        return $"{color}{player.Player.Name}[silver] ([lime]{wins} Wins[silver])";
    }

    private void SendCountdownMessage(ISpecialDay specialDay, int remaining)
    {
        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid)
                continue;

            var message = player.Localizer["special_day_countdown_html", specialDay.Name, remaining, specialDay.Description];
            player.Player.SendCenterHTML(message, 1500);
        }
    }

    private void StartActiveHud(ISpecialDay specialDay)
    {
        StopActiveHud();
        SendActiveHudMessage(specialDay);

        _activeHudCts = _core.Scheduler.RepeatBySeconds(1f, () =>
        {
            if (CurrentSpecialDay != specialDay || !_currentDayStarted)
            {
                StopActiveHud();
                return;
            }

            SendActiveHudMessage(specialDay);
        });
    }

    private void SendActiveHudMessage(ISpecialDay specialDay)
    {
        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid)
                continue;

            var message = player.Localizer["special_day_active_html", specialDay.Name, string.Empty, specialDay.Description];
            player.Player.SendCenterHTML(message, 1500);
        }
    }

    private IMenuAPI PrimaryGunsMenu(IJBPlayer player, IReadOnlyList<ItemDefinitionIndex> primaryWeapons, IReadOnlyList<ItemDefinitionIndex> secondaryWeapons)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["special_guns_primary_menu.title"]);

        foreach (var weapon in primaryWeapons)
        {
            AddButton(builder, GetWeaponLabel(weapon), () =>
            {
                _core.MenusAPI.OpenMenuForPlayer(player.Player, SecondaryGunsMenu(player, weapon, secondaryWeapons));
            });
        }

        return builder.Build();
    }

    private IMenuAPI SecondaryGunsMenu(IJBPlayer player, ItemDefinitionIndex primaryWeapon, IReadOnlyList<ItemDefinitionIndex> secondaryWeapons)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["special_guns_secondary_menu.title"]);

        foreach (var weapon in secondaryWeapons)
        {
            AddButton(builder, GetWeaponLabel(weapon), () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    GiveSelectedGuns(player.Player, primaryWeapon, weapon);
                    player.SendMessage(MessageType.Chat, "special_guns_given", true, args: [GetWeaponLabel(primaryWeapon), GetWeaponLabel(weapon)]);
                    _core.MenusAPI.CloseActiveMenu(player.Player);
                });
            });
        }

        return builder.Build();
    }

    private void FreezePlayers(ISpecialDay specialDay, Color? color = null)
    {
        foreach (var player in GetFreezePlayers(specialDay))
            FreezePlayer(player, color);
    }

    private void UnfreezePlayers()
    {
        _countdownFreezeActive = false;

        foreach (var playerKey in _frozenPlayerKeys.ToList())
        {
            var player = FindPlayerByKey(playerKey);
            if (player == null)
            {
                _frozenPlayerKeys.Remove(playerKey);
                continue;
            }

            PlayerUtils.UnfreezeVelocity(player.Player, new Color(255, 255, 255, 255));
            _frozenPlayerKeys.Remove(playerKey);
        }
    }

    private void StopCountdown()
    {
        _countdownCts?.Cancel();
        _countdownCts = null;
    }

    private void StopActiveHud()
    {
        _activeHudCts?.Cancel();
        _activeHudCts = null;
    }

    private void FreezePlayer(IJBPlayer player, Color? color = null)
    {
        if (!player.Player.IsValid || !player.Player.IsAlive)
            return;

        PlayerUtils.FreezeVelocity(player.Player, color);
        _frozenPlayerKeys.Add(PlayerIdentity.GetKey(player.Player));
    }

    private IJBPlayer? FindPlayerByKey(ulong playerKey)
    {
        return _players.GetAllPlayers().FirstOrDefault(player => PlayerIdentity.GetKey(player.Player) == playerKey);
    }

    private IEnumerable<IJBPlayer> GetFreezePlayers(ISpecialDay specialDay)
    {
        return specialDay.FreezeTeamOnCountdown switch
        {
            SpecialDayFreezeTeam.None => [],
            SpecialDayFreezeTeam.Prisoners => _players.GetPlayersByTeam(JBTeam.Prisoner),
            SpecialDayFreezeTeam.Guards => _players.GetPlayersByTeam(JBTeam.Guard),
            _ => _players.GetAllPlayers()
        };
    }

    private void ApplyStartLoadout(ISpecialDay specialDay)
    {
        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid || !player.Player.IsAlive)
                continue;

            if (specialDay.StripWeaponsOnStart)
                StripWeapons(player.Player);

            foreach (var weaponName in specialDay.GiveWeaponsOnStart)
                GiveWeapon(player.Player, weaponName);
        }
    }

    private void GiveSelectedGuns(IPlayer player, ItemDefinitionIndex primaryWeapon, ItemDefinitionIndex secondaryWeapon)
    {
        if (!player.IsValid || !player.IsAlive)
            return;

        StripWeapons(player);

        var primaryClassname = _core.Helpers.GetClassnameByDefinitionIndex(primaryWeapon);
        var secondaryClassname = _core.Helpers.GetClassnameByDefinitionIndex(secondaryWeapon);

        if (!string.IsNullOrEmpty(primaryClassname))
            GiveWeapon(player, primaryClassname);

        if (!string.IsNullOrEmpty(secondaryClassname))
            GiveWeapon(player, secondaryClassname);

        string knife = player.Controller.Team == Team.T ? "weapon_knife_t" : "weapon_knife";
        GiveWeapon(player, knife);
    }

    private IEnumerable<ItemDefinitionIndex> GetMenuWeapons(ISpecialDay specialDay, IReadOnlySet<ItemDefinitionIndex> group)
    {
        var weapons = specialDay.GunsMenuWeapons.Count > 0
            ? specialDay.GunsMenuWeapons
            : specialDay.AllowAllWeapons
                ? SpecialDayWeapons.GunsMenuWeapons.ToArray()
                : [];

        return weapons
            .Where(group.Contains)
            .Where(weapon => specialDay.AllowAllWeapons || specialDay.AllowedWeapons.Contains(weapon));
    }

    private string GetWeaponLabel(ItemDefinitionIndex weapon)
    {
        var classname = _core.Helpers.GetClassnameByDefinitionIndex(weapon);
        if (string.IsNullOrEmpty(classname))
            return weapon.ToString();

        return classname
            .Replace("weapon_", "", StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .ToUpperInvariant();
    }

    private static void AddButton(IMenuBuilderAPI builder, string label, Action action)
    {
        var option = new ButtonMenuOption(label);
        option.Click += (_, _) =>
        {
            action();
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }

    private static void StripWeapons(IPlayer player)
    {
        var weaponServices = player.PlayerPawn?.WeaponServices;
        if (weaponServices == null)
            return;

        foreach (var weapon in weaponServices.MyValidWeapons.ToList())
        {
            if (weapon?.IsValid == true)
                weaponServices.RemoveWeapon(weapon);
        }
    }

    private static void GiveWeapon(IPlayer player, string weaponName)
    {
        var pawn = player.Pawn;
        if (pawn == null || !pawn.IsValid)
            return;

        pawn.ItemServices?.GiveItem<CBaseEntity>(weaponName);
    }

    private void Unhook(ref Guid? hookId)
    {
        if (!hookId.HasValue)
            return;

        _core.GameEvent.Unhook(hookId.Value);
        hookId = null;
    }
}
