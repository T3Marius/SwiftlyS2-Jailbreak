using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Sounds;

namespace Jailbreak;

public sealed class LastRequestManager
{
    public bool WasLastRequestActiveThisFrame { get; private set; }

    private const float DuelBeamWidth = 2.4f;
    private const float DuelBeamHeight = 48f;

    private static readonly Color PrisonerBeaconColor = new(255, 190, 55, 235);
    private static readonly Color GuardBeaconColor = new(80, 170, 255, 235);
    private static readonly Color DuelBeamColor = new(255, 255, 255, 210);

    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly BeaconManager _beaconManager;
    private readonly CuffsManager _cuffsManager;
    private readonly ILogger<LastRequestManager> _log;
    private readonly Dictionary<string, ILastRequest> _lastRequests = new(StringComparer.OrdinalIgnoreCase);

    private Guid? _playerDeathHookId;
    private Guid? _playerDisconnectHookId;
    private Guid? _roundEndHookId;
    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _blipSoundCts;
    private LastRequestStartContext? _currentContext;
    private CHandle<CBeam>? _duelBeamHandle;
    private bool _currentStarted;
    private bool _countdownActive;

    public LastRequestManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        BeaconManager beaconManager,
        CuffsManager cuffsManager,
        ILogger<LastRequestManager> log)
    {
        _core = core;
        _players = players;
        _beaconManager = beaconManager;
        _cuffsManager = cuffsManager;
        _log = log;
    }

    public IReadOnlyCollection<ILastRequest> LastRequests => _lastRequests.Values.ToArray();
    public ILastRequest? CurrentLastRequest { get; private set; }
    public bool IsLastRequestActive => CurrentLastRequest != null;
    public bool IsCountdownActive => _countdownActive;

    public void Register()
    {
        _playerDeathHookId = _core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
        _playerDisconnectHookId = _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        _roundEndHookId = _core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        _core.Event.OnEntityTakeDamage += OnEntityTakeDamage;
        _core.Event.OnMapUnload += OnMapUnload;
        _core.Event.OnTick += OnTick;
    }

    public void Unregister()
    {
        Unhook(ref _playerDeathHookId);
        Unhook(ref _playerDisconnectHookId);
        Unhook(ref _roundEndHookId);
        _core.Event.OnEntityTakeDamage -= OnEntityTakeDamage;
        _core.Event.OnMapUnload -= OnMapUnload;
        _core.Event.OnTick -= OnTick;

        EndLastRequest(null, null, announce: false);
        _lastRequests.Clear();
    }

    public bool RegisterLastRequest(ILastRequest lastRequest)
    {
        if (string.IsNullOrWhiteSpace(lastRequest.Id))
            throw new ArgumentException("Last Request id cannot be empty.", nameof(lastRequest));

        if (!_lastRequests.TryAdd(lastRequest.Id, lastRequest))
        {
            _log.LogWarning("Last Request registration skipped because id is already registered. Id={Id}", lastRequest.Id);
            return false;
        }

        _log.LogInformation("Registered Last Request. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
        return true;
    }

    public bool UnregisterLastRequest(string id)
    {
        if (!_lastRequests.TryGetValue(id, out var lastRequest))
            return false;

        if (ReferenceEquals(CurrentLastRequest, lastRequest))
            EndLastRequest(null, null);

        _lastRequests.Remove(id);
        _log.LogInformation("Unregistered Last Request. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
        return true;
    }

    public bool TryGetLastRequest(string id, out ILastRequest lastRequest)
    {
        return _lastRequests.TryGetValue(id, out lastRequest!);
    }

    public bool StartLastRequest(ILastRequest lastRequest, LastRequestStartContext context)
    {
        if (CurrentLastRequest != null || !lastRequest.CanStart(context))
            return false;

        CurrentLastRequest = lastRequest;
        _currentContext = context;
        _currentStarted = false;

        _blipSoundCts?.Cancel();
        _blipSoundCts = null;

        DisableCurrentWarden();
        ApplyVisuals(context);
        AnnounceLastRequestSelected(lastRequest, context);

        if (lastRequest.StartCountdown <= 0)
        {
            StartCurrentLastRequest(lastRequest, context);
            return true;
        }

        StartCountdown(lastRequest, context);

        _blipSoundCts = _core.Scheduler.RepeatBySeconds(1.0f, () =>
        {
            foreach (var player in _players.GetAllPlayers())
                player.Player.ExecuteCommand("play sounds/buttons/blip1.vsnd_c");

            if (!IsLastRequestActive)
            {
                _blipSoundCts?.Cancel();
                _blipSoundCts = null;
                return;
            }
        });

        return true;
    }

    public void EndLastRequest(IJBPlayer? winner, IJBPlayer? loser)
    {
        EndLastRequest(winner, loser, announce: true);
    }

    public bool CanUseLastRequest(IJBPlayer player)
    {
        return CurrentLastRequest == null
            && player.Team == JBTeam.Prisoner
            && player.Player.IsValid
            && player.Player.IsAlive
            && !player.IsRebel
            && _players.GetPlayersByTeam(JBTeam.Prisoner).Count(IsAlive) == 1;
    }

    private void StartCountdown(ILastRequest lastRequest, LastRequestStartContext context)
    {
        _countdownActive = true;
        var remaining = lastRequest.StartCountdown;

        SendCountdownMessage(lastRequest, context, remaining);
        StopCountdown();
        _countdownCts = _core.Scheduler.RepeatBySeconds(1f, () =>
        {
            if (CurrentLastRequest != lastRequest || _currentContext != context)
            {
                StopCountdown();
                return;
            }

            remaining--;
            if (remaining <= 0)
            {
                StopCountdown();
                StartCurrentLastRequest(lastRequest, context);
                return;
            }

            SendCountdownMessage(lastRequest, context, remaining);
        });
    }

    private void StartCurrentLastRequest(ILastRequest lastRequest, LastRequestStartContext context)
    {
        _countdownActive = false;

        try
        {
            ApplyStartLoadout(lastRequest, context);
            lastRequest.Start(context);
            _currentStarted = true;
            _players.SendMessage(MessageType.Chat, "last_request_started", true, args: [lastRequest.Name, context.Prisoner.Player.Name]);
            _log.LogInformation("Started Last Request. Id={Id}, Name={Name}, Prisoner={Prisoner}, Guard={Guard}",
                lastRequest.Id,
                lastRequest.Name,
                context.Prisoner.SteamID,
                context.Guard?.SteamID);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start Last Request. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
            EndLastRequest(null, null, announce: false);
        }
    }

    private void EndLastRequest(IJBPlayer? winner, IJBPlayer? loser, bool announce)
    {
        StopCountdown();

        var lastRequest = CurrentLastRequest;
        if (lastRequest == null)
            return;

        CurrentLastRequest = null;
        _countdownActive = false;

        if (_currentStarted)
            lastRequest.End(winner, loser);

        _currentStarted = false;
        CleanupVisuals();
        _currentContext = null;

        if (announce)
            AnnounceLastRequestEnded(lastRequest, winner);

        _log.LogInformation("Ended Last Request. Id={Id}, Name={Name}, Winner={Winner}, Loser={Loser}",
            lastRequest.Id,
            lastRequest.Name,
            winner?.SteamID,
            loser?.SteamID);
    }

    private HookResult OnPlayerDeath(EventPlayerDeath e)
    {
        var lastRequest = CurrentLastRequest;
        if (lastRequest == null || _currentContext == null || e.UserIdPlayer == null)
            return HookResult.Continue;

        var victim = _players.SyncPlayer(e.UserIdPlayer);
        var attacker = e.AttackerPlayer == null ? null : _players.SyncPlayer(e.AttackerPlayer);
        if (victim == null || !ShouldEndOnDeath(_currentContext, victim))
            return HookResult.Continue;

        var winner = GetDeathWinner(_currentContext, victim, attacker);

        if (_currentStarted)
        {
            lastRequest.OnPlayerDied(victim, attacker);
            _currentStarted = false;
        }

        EndLastRequest(winner, victim);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e)
    {
        var lastRequest = CurrentLastRequest;
        if (lastRequest == null || e.UserIdPlayer == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(e.UserIdPlayer);
        if (player == null || _currentContext == null || !ShouldEndOnDisconnect(_currentContext, player))
            return HookResult.Continue;

        var winner = GetDisconnectWinner(_currentContext, player);

        if (_currentStarted)
        {
            lastRequest.OnPlayerDisconnected(player);
            _currentStarted = false;
        }

        EndLastRequest(winner, player);
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd e)
    {
        EndLastRequest(null, null, announce: false);
        return HookResult.Continue;
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        EndLastRequest(null, null, announce: false);
    }

    private void OnEntityTakeDamage(IOnEntityTakeDamageEvent e)
    {
        var lastRequest = CurrentLastRequest;
        if (lastRequest == null || _currentContext == null)
            return;

        var rawVictim = GetPlayerFromEntity(e.Entity);
        if (rawVictim == null)
            return;

        var victim = _players.SyncPlayer(rawVictim);
        if (victim == null || !IsParticipant(lastRequest, _currentContext, victim))
            return;

        var attackerPawn = e.Info.AttackerInfo.AttackerPawn.Value;
        var rawAttacker = attackerPawn?.ToPlayer();
        var attacker = rawAttacker == null ? null : _players.SyncPlayer(rawAttacker);

        if (_countdownActive || attacker == null || !IsParticipant(lastRequest, _currentContext, attacker))
            BlockDamage(e);
    }

    private void OnTick()
    {
        WasLastRequestActiveThisFrame = IsLastRequestActive;
        UpdateDuelBeam();
    }

    private void DisableCurrentWarden()
    {
        var warden = _players.GetWarden();
        if (warden != null)
        {
            _cuffsManager.OnWardenRemove(warden);
            warden.SetWarden(false, silent: true);
        }

        foreach (var player in _players.GetAllPlayers())
            player.CanBecomeWarden = false;
    }

    private void ApplyVisuals(LastRequestStartContext context)
    {
        _beaconManager.StartPlayerBeacon(context.Prisoner.Player, PrisonerBeaconColor, rainbow: false);

        if (context.Guard != null)
            _beaconManager.StartPlayerBeacon(context.Guard.Player, GuardBeaconColor, rainbow: false);

        if (context.Guard != null)
            EnsureDuelBeam();
    }

    private void CleanupVisuals()
    {
        if (_currentContext != null)
        {
            _beaconManager.StopPlayerBeacon(_currentContext.Prisoner.SteamID);

            if (_currentContext.Guard != null)
                _beaconManager.StopPlayerBeacon(_currentContext.Guard.SteamID);
        }

        RemoveDuelBeam();
    }

    private void EnsureDuelBeam()
    {
        if (_duelBeamHandle?.Value?.IsValid == true)
            return;

        var beam = _core.EntitySystem.CreateEntity<CBeam>();
        beam.DispatchSpawn();
        ConfigureDuelBeam(beam);
        _duelBeamHandle = _core.EntitySystem.GetRefEHandle(beam);
    }

    private void UpdateDuelBeam()
    {
        if (CurrentLastRequest == null || _currentContext?.Guard == null)
            return;

        var beam = _duelBeamHandle?.Value;
        if (beam?.IsValid != true)
        {
            EnsureDuelBeam();
            beam = _duelBeamHandle?.Value;
            if (beam?.IsValid != true)
                return;
        }

        if (!TryGetBeamPoint(_currentContext.Prisoner, out var start) || !TryGetBeamPoint(_currentContext.Guard, out var end))
            return;

        beam.Teleport(start, null, null);
        beam.EndPos = end;
        beam.EndPosUpdated();
    }

    private void RemoveDuelBeam()
    {
        var beam = _duelBeamHandle?.Value;
        if (beam?.IsValid == true)
            beam.Despawn();

        _duelBeamHandle = null;
    }

    private static bool TryGetBeamPoint(IJBPlayer player, out Vector point)
    {
        point = Vector.Zero;

        if (!player.Player.IsValid || player.Player.PlayerPawn == null)
            return false;

        var origin = player.Player.PlayerPawn.AbsOrigin;
        if (origin == null)
            return false;

        point = new Vector(origin.Value.X, origin.Value.Y, origin.Value.Z + DuelBeamHeight);
        return true;
    }

    private static void ConfigureDuelBeam(CBeam beam)
    {
        beam.BeamType = BeamType_t.BEAM_POINTS;
        beam.NumBeamEnts = 2;
        beam.Width = DuelBeamWidth;
        beam.EndWidth = DuelBeamWidth;
        beam.FadeLength = 0f;
        beam.HaloScale = 0f;
        beam.Amplitude = 0f;
        beam.Speed = 0f;
        beam.FrameRate = 0f;
        beam.ClipStyle = BeamClipStyle_t.kNOCLIP;
        beam.TurnedOff = false;
        beam.RenderMode = RenderMode_t.kRenderTransAlpha;
        beam.RenderFX = RenderFx_t.kRenderFxNone;
        beam.Render = DuelBeamColor;

        beam.BeamTypeUpdated();
        beam.NumBeamEntsUpdated();
        beam.WidthUpdated();
        beam.EndWidthUpdated();
        beam.FadeLengthUpdated();
        beam.HaloScaleUpdated();
        beam.AmplitudeUpdated();
        beam.SpeedUpdated();
        beam.FrameRateUpdated();
        beam.ClipStyleUpdated();
        beam.TurnedOffUpdated();
        beam.RenderModeUpdated();
        beam.RenderFXUpdated();
        beam.RenderUpdated();
    }

    private void SendCountdownMessage(ILastRequest lastRequest, LastRequestStartContext context, int remaining)
    {
        var variant = context.SelectedVariant == null
            ? _core.Localizer["none"]
            : string.IsNullOrWhiteSpace(context.SelectedVariant.Description)
                ? context.SelectedVariant.Name
                : $"{context.SelectedVariant.Name}: {context.SelectedVariant.Description}";

        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid)
                continue;

            var message = player.Localizer["last_request_countdown_html", lastRequest.Name, remaining, lastRequest.Description, variant];
            player.Player.SendCenterHTML(message, 1100);
        }
    }

    private void StopCountdown()
    {
        _countdownCts?.Cancel();
        _countdownCts = null;
    }

    private void ApplyStartLoadout(ILastRequest lastRequest, LastRequestStartContext context)
    {
        _core.Scheduler.NextWorldUpdate(() =>
        {
            foreach (var player in GetLoadoutPlayers(lastRequest, context))
            {
                if (!player.Player.IsValid || !player.Player.IsAlive)
                    continue;

                if (lastRequest.StripWeaponsOnStart)
                    StripWeapons(player.Player);

                foreach (var weaponName in lastRequest.GiveWeaponsOnStart)
                    GiveWeapon(player.Player, weaponName);

                if (context.SelectedWeapon.HasValue)
                    GiveSelectedWeapon(player.Player, context.SelectedWeapon.Value);
            }
        });
    }

    private IEnumerable<IJBPlayer> GetLoadoutPlayers(ILastRequest lastRequest, LastRequestStartContext context)
    {
        yield return context.Prisoner;

        if (context.Guard != null)
        {
            yield return context.Guard;
            yield break;
        }

        if (lastRequest.OpponentMode == LastRequestOpponentMode.PrisonerVsAllGuards)
        {
            foreach (var guard in _players.GetPlayersByTeam(JBTeam.Guard).Where(IsAlive))
                yield return guard;
        }
    }

    private void GiveSelectedWeapon(IPlayer player, ItemDefinitionIndex weapon)
    {
        var classname = _core.Helpers.GetClassnameByDefinitionIndex(weapon);
        if (!string.IsNullOrEmpty(classname))
            GiveWeapon(player, classname);
    }

    private void AnnounceLastRequestSelected(ILastRequest lastRequest, LastRequestStartContext context)
    {
        var opponent = context.Guard == null
            ? _core.Localizer["last_request_all_guards"]
            : FormatPlayerName(context.Guard);
        var variant = context.SelectedVariant?.Name ?? _core.Localizer["none"];

        _players.SendMessage(MessageType.Chat, "last_request_selected", true, args: [FormatPlayerName(context.Prisoner), opponent, lastRequest.Name, variant]);
    }

    private void AnnounceLastRequestEnded(ILastRequest lastRequest, IJBPlayer? winner)
    {
        var winnerName = winner == null ? _core.Localizer["none"] : FormatPlayerName(winner);
        _players.SendMessage(MessageType.Chat, "last_request_ended", true, args: [lastRequest.Name, winnerName]);
    }

    private static bool IsParticipant(ILastRequest lastRequest, LastRequestStartContext context, IJBPlayer player)
    {
        return player.SteamID == context.Prisoner.SteamID
            || context.Guard?.SteamID == player.SteamID
            || (lastRequest.OpponentMode == LastRequestOpponentMode.PrisonerVsAllGuards && player.Team == JBTeam.Guard);
    }

    private static bool ShouldEndOnDeath(LastRequestStartContext context, IJBPlayer victim)
    {
        if (victim.SteamID == context.Prisoner.SteamID)
            return true;

        return context.Guard?.SteamID == victim.SteamID;
    }

    private static bool ShouldEndOnDisconnect(LastRequestStartContext context, IJBPlayer player)
    {
        if (player.SteamID == context.Prisoner.SteamID)
            return true;

        return context.Guard?.SteamID == player.SteamID;
    }

    private static IJBPlayer? GetDisconnectWinner(LastRequestStartContext context, IJBPlayer disconnectedPlayer)
    {
        if (disconnectedPlayer.SteamID == context.Prisoner.SteamID)
            return context.Guard;

        return context.Prisoner;
    }

    private static IJBPlayer? GetDeathWinner(LastRequestStartContext context, IJBPlayer victim, IJBPlayer? attacker)
    {
        if (victim.SteamID == context.Prisoner.SteamID)
            return attacker ?? context.Guard;

        if (context.Guard?.SteamID == victim.SteamID)
            return context.Prisoner;

        return attacker;
    }

    private static bool IsAlive(IJBPlayer player)
    {
        return player.Player.IsValid && player.Player.IsAlive;
    }

    private static string FormatPlayerName(IJBPlayer player)
    {
        var color = player.Team switch
        {
            JBTeam.Guard => "[blue]",
            JBTeam.Prisoner => "[gold]",
            _ => "[silver]"
        };

        return $"{color}{player.Player.Name}[silver]";
    }

    private static void BlockDamage(IOnEntityTakeDamageEvent e)
    {
        e.Info.Damage = 0;
        e.Info.TotalledDamage = 0;
        e.DamageResult.DamageDealt = 0;
        e.Result = HookResult.Stop;
    }

    private IPlayer? GetPlayerFromEntity(CEntityInstance entity)
    {
        var pawn = entity.As<CCSPlayerPawn>();
        if (pawn.IsValid)
            return pawn.ToPlayer();

        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player.PlayerPawn?.Address == entity.Address)
                return player;
        }

        return null;
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
