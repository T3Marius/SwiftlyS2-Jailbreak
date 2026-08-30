using HudText.Contract;
using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class Events
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly CellManager _cellManager;
    private readonly BoxManager _boxManager;
    private readonly CuffsManager _cuffsManager;
    private readonly DrawManager _drawManager;
    private readonly SpecialDayManager _specialDayManager;
    private readonly LastRequestManager _lastRequestManager;
    private readonly GuardGunsManager _guardGunsManager;
    private readonly WardenTagManager _wardenTagManager;
    private readonly JailbreakSoundManager _soundManager;

    /* -------------------   Hud   ------------------- */
    private IHudTextService? _hudText;
    private HudTextHandle? _currentWardenHud;
    private HudTextHandle? _roundWinnerHud;

    /* ------------------- Configs ------------------- */
    private readonly WardenConfig _wardenConfig;
    private readonly ModelsConfig _modelsConfig;
    private readonly UtilsConfig _utilsConfig;
    private readonly VoiceConfig _voiceConfig;
    private readonly HudConfig _hudConfig;
    /* ----------------------------------------------- */

    /* -------------- Game Events -------------- */
    private Guid? _playerSpawnHookId;
    private Guid? _playerConnectFullHookId;
    private Guid? _playerTeamChangeHookId;
    private Guid? _playerDisconnectHookId;
    private Guid? _roundStartHookId;
    private Guid? _roundEndHookId;
    private Guid? _playerDeathHookId;
    /* ------------------------------------------ */

    private CancellationTokenSource? _wardenCheckCts;
    private CancellationTokenSource? _doorsCheckCts;
    private CancellationTokenSource? _checkPrisonersVoiceCts;
    private readonly Random _random = new();
    private bool _isRoundEnding;
    private bool _isMapUnloading;

    public Events(
        ISwiftlyCore core,
        IJBPlayerManagement playerManagement,
        CellManager cellManager,
        BoxManager boxManager,
        CuffsManager cuffsManager,
        DrawManager drawManager,
        SpecialDayManager specialDayManager,
        LastRequestManager lastRequestManager,
        GuardGunsManager guardGunsManager,
        WardenTagManager wardenTagManager,
        JailbreakSoundManager soundManager,
        IOptions<WardenConfig> wardenConfig,
        IOptions<ModelsConfig> modelsConfig,
        IOptions<UtilsConfig> utilsConfig,
        IOptions<VoiceConfig> voiceConfig,
        IOptions<HudConfig> hudConfig)
    {
        _core = core;
        _players = playerManagement;
        _cellManager = cellManager;
        _boxManager = boxManager;
        _cuffsManager = cuffsManager;
        _drawManager = drawManager;
        _specialDayManager = specialDayManager;
        _lastRequestManager = lastRequestManager;
        _guardGunsManager = guardGunsManager;
        _wardenTagManager = wardenTagManager;
        _soundManager = soundManager;
        _wardenConfig = wardenConfig.Value;
        _modelsConfig = modelsConfig.Value;
        _utilsConfig = utilsConfig.Value;
        _voiceConfig = voiceConfig.Value;
        _hudConfig = hudConfig.Value;
    }

    public void Register()
    {
        _players.CurrentCtRolesChanged += RefreshCurrentCtRolesDisplay;
        _specialDayManager.StateChanged += OnSpecialDayStateChanged;
        _lastRequestManager.StateChanged += RefreshCurrentCtRolesDisplay;
        _playerSpawnHookId = _core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        _playerConnectFullHookId = _core.GameEvent.HookPost<EventPlayerConnectFull>(OnPlayerConnectFull);
        _playerTeamChangeHookId = _core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeamChange);
        _playerDisconnectHookId = _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        _roundStartHookId = _core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        _roundEndHookId = _core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        _playerDeathHookId = _core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
        _core.Event.OnMapUnload += OnMapUnload;

        RefreshCurrentCtRolesDisplay();
    }
    public void Unregister()
    {
        _players.CurrentCtRolesChanged -= RefreshCurrentCtRolesDisplay;
        _specialDayManager.StateChanged -= OnSpecialDayStateChanged;
        _lastRequestManager.StateChanged -= RefreshCurrentCtRolesDisplay;

        // Map unload already destroys CustomHud entities. Calling into HUD natives while
        // that teardown is in progress can crash the server.
        if (!_isMapUnloading)
        {
            RemoveCurrentWardenHud();
            RemoveRoundWinnerHud();
        }
        else
        {
            _currentWardenHud = null;
            _roundWinnerHud = null;
        }
        Unhook(ref _playerSpawnHookId);
        Unhook(ref _playerConnectFullHookId);
        Unhook(ref _playerTeamChangeHookId);
        Unhook(ref _playerDisconnectHookId);
        Unhook(ref _roundStartHookId);
        Unhook(ref _roundEndHookId);
        Unhook(ref _playerDeathHookId);
        _core.Event.OnMapUnload -= OnMapUnload;

        _wardenCheckCts?.Cancel();
        _wardenCheckCts = null;

        _doorsCheckCts?.Cancel();
        _doorsCheckCts = null;

        StopCheckPrisonerVoiceTimer();
    }
    public void SetHudTextService(IHudTextService? hudText)
    {
        if (ReferenceEquals(_hudText, hudText))
            return;

        RemoveCurrentWardenHud();
        RemoveRoundWinnerHud();

        _hudText = hudText;
        _currentWardenHud = null;
    }
    private void RefreshCurrentCtRolesDisplay()
    {
        if (_isMapUnloading || _hudText is null)
            return;

        // A queued day starts next round; it must not hide the roles HUD before then.
        if (_specialDayManager.IsSpecialDayActive || _lastRequestManager.IsLastRequestActive)
        {
            if (_currentWardenHud is not null)
                TryHideCurrentWardenHud();

            return;
        }

        var warden = _players.GetWarden()?.Player.Name ?? _core.Localizer["none"];
        var deputy = _players.GetDeputy()?.Player.Name ?? _core.Localizer["none"];

        // A global HudText handle has one string for everyone.
        var text = _core.Localizer["current_ct_roles.hud", warden, deputy];

        if (_currentWardenHud is null)
        {
            CreateCurrentWardenHud(text);
            return;
        }

        try
        {
            _hudText.ShowHud(_currentWardenHud.Value);
            _hudText.UpdateHud(_currentWardenHud.Value, text);
        }
        catch (ArgumentException)
        {
            _currentWardenHud = null;
            CreateCurrentWardenHud(text);
        }
    }

    private void CreateCurrentWardenHud(string text)
    {
        var style = _hudConfig.CurrentWardenAndDeputyHud;
        _currentWardenHud = _hudText!.CreateHud(text, new HudTextOptions
        {
            Position = style.Position,
            Color = style.Color,
            Size = style.Size,
            Background = style.Background,
            BackgroundOpacity = style.BackgroundOpacity,
            DropShadow = style.DropShadow,
            OutlineColor = style.OutlineColor,
            Font = style.Font,
            FontWeight = style.FontWeight,
            TextAlignment = style.TextAlignment,
        });
    }

    private void TryHideCurrentWardenHud()
    {
        try
        {
            _hudText!.HideHud(_currentWardenHud!.Value);
        }
        catch (ArgumentException)
        {
            _currentWardenHud = null;
        }
    }

    private void RemoveCurrentWardenHud()
    {
        if (_hudText is not null && _currentWardenHud is not null)
        {
            try
            {
                _hudText.RemoveHud(_currentWardenHud.Value);
            }
            catch (ArgumentException)
            {
                // HudText already discarded the handle during a map change.
            }
        }

        _currentWardenHud = null;
    }

    private void OnMapUnload(IOnMapUnloadEvent _)
    {
        _isMapUnloading = true;
        // The map is destroying CustomHud entities. Do not issue HUD native calls here.
        _currentWardenHud = null;
        _roundWinnerHud = null;
    }

    private void OnSpecialDayStateChanged()
    {
        if (_isMapUnloading)
            return;

        RefreshCurrentCtRolesDisplay();
        // Special-day end commonly occurs during round-end callbacks. Recheck after that cleanup
        // so the roles HUD is reliably recreated when the day is no longer active.
        _core.Scheduler.NextWorldUpdate(RefreshCurrentCtRolesDisplay);
    }
    private HookResult OnPlayerSpawn(EventPlayerSpawn e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(e.UserIdPlayer);
        if (player == null)
            return HookResult.Continue;

        ApplyTeamLoadout(player);
        return HookResult.Continue;
    }
    private HookResult OnPlayerConnectFull(EventPlayerConnectFull e)
    {
        // Ensure the HUD is created/shown as soon as a player is fully connected,
        // instead of waiting for a warden/deputy change.
        RefreshCurrentCtRolesDisplay();
        return HookResult.Continue;
    }
    private HookResult OnPlayerTeamChange(EventPlayerTeam e)
    {
        var rawPlayer = e.UserIdPlayer;
        if (rawPlayer == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(rawPlayer);
        if (player == null)
            return HookResult.Continue;

        ApplyTeamLoadout(player);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            var syncedPlayer = _players.SyncPlayer(rawPlayer);
            if (syncedPlayer != null)
                ApplyTeamLoadout(syncedPlayer);
        });

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        _drawManager.CleanupPlayer(e.UserIdPlayer);
        _cuffsManager.CleanupPlayer(e.UserIdPlayer);
        _players.RemovePlayer(e.UserIdPlayer);
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart e)
    {
        _isMapUnloading = false;
        _isRoundEnding = false;
        RemoveRoundWinnerHud();
        _drawManager.ClearRoundAccess();

        foreach (var p in _players.GetAllPlayers())
            p.WasUnmutedByWarden = false;

        foreach (var p in _players.GetPlayersByRole(JBRole.Freeday))
            p.SetFreeday(false);

        foreach (var p in _players.GetPlayersByRole(JBRole.Rebel))
            p.SetRebel(false);

        if (!_specialDayManager.HasQueuedOrActiveSpecialDay)
        {
            foreach (var prisoner in _players.GetPlayersByTeam(JBTeam.Prisoner))
                StripPrisonerWeapons(prisoner);
        }

        if (_voiceConfig.KeepPrisonersMutedDuringRound)
        {
            foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner).Where(p => !p.IsMuted))
            {
                if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                    continue;

                p.Mute();
            }

            StartCheckPrisonerVoiceTimer();
        }
        else if (_voiceConfig.KeepPrisonersMutedForSecondsOnRoundStart > 0)
        {
            foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner).Where(p => !p.IsMuted))
            {
                if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                    continue;

                p.Mute();
                _players.SendMessage(MessageType.Chat, "prisoners_muted_roundstart", true, args: [_voiceConfig.KeepPrisonersMutedForSecondsOnRoundStart]);
            }

            _core.Scheduler.DelayBySeconds(_voiceConfig.KeepPrisonersMutedForSecondsOnRoundStart, () =>
            {
                foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner).Where(p => p.IsMuted))
                {
                    if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                        continue;

                    p.Unmute();
                    _players.SendMessage(MessageType.Chat, "prisoners_unmuted", true);
                }
            });
        }
        _cellManager.CellsOpen = false;
        _boxManager.BoxEnabled = false;

        _wardenCheckCts?.Cancel();
        _wardenCheckCts = null;

        _doorsCheckCts?.Cancel();
        _doorsCheckCts = null;

        var currentWarden = _players.GetWarden();
        if (currentWarden != null)
        {
            _cuffsManager.OnWardenRemove(currentWarden);
            currentWarden.SetWarden(false, silent: true);
            _wardenTagManager.RefreshNow();
        }

        var specialDayRound = _specialDayManager.HasQueuedOrActiveSpecialDay;
        var normalWardenBlocked = specialDayRound || _lastRequestManager.IsLastRequestActive;

        foreach (var player in _players.GetAllPlayers())
            player.CanBecomeWarden = !normalWardenBlocked && player.Team == JBTeam.Guard;

        // Keep the HUD alive/refreshed across round restarts.
        RefreshCurrentCtRolesDisplay();

        if (normalWardenBlocked)
            return HookResult.Continue;

        StartWardenCheckTimer();

        _doorsCheckCts = _core.Scheduler.DelayBySeconds(_utilsConfig.OpenCellsAfterSeconds, () =>
        {
            if (_cellManager.CellsOpen)
            {
                _doorsCheckCts?.Cancel();
                _doorsCheckCts = null;
                return;
            }

            _cellManager.OpenCells();
            _cellManager.CellsOpen = true;

            _players.SendMessage(MessageType.Chat, "cells_opened_automatically", true);

            _doorsCheckCts?.Cancel();
            _doorsCheckCts = null;
        });

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd e)
    {
        _isRoundEnding = true;
        ShowRoundWinnerHud(e);

        _wardenCheckCts?.Cancel();
        _wardenCheckCts = null;

        _doorsCheckCts?.Cancel();
        _doorsCheckCts = null;

        foreach (var p in _players.GetAllPlayers())
            p.WasUnmutedByWarden = false;

        foreach (var p in _players.GetPlayersByRole(JBRole.Freeday))
            p.SetFreeday(false);

        foreach (var p in _players.GetPlayersByRole(JBRole.Rebel))
            p.SetRebel(false);

        StopCheckPrisonerVoiceTimer();

        if (_voiceConfig.UnmutePrisonersOnRoundEnd)
        {
            foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner).Where(p => p.IsMuted))
                p.Unmute();
        }

        _boxManager.StopBox();
        var currentWarden = _players.GetWarden();
        if (currentWarden != null)
        {
            _cuffsManager.OnWardenRemove(currentWarden);
            currentWarden.SetWarden(false, silent: true);
        }

        var currentDeputy = _players.GetDeputy();
        currentDeputy?.SetDeputy(false, silent: true);

        _wardenTagManager.RefreshNow();
        _cuffsManager.CleanupAll();

        foreach (var player in _players.GetAllPlayers())
        {
            player.CanBecomeWarden = false;
        }

        return HookResult.Continue;
    }

    private void ShowRoundWinnerHud(EventRoundEnd e)
    {
        if (_isMapUnloading || _hudText is null)
            return;

        var specialDay = _specialDayManager.CurrentSpecialDay;
        if (specialDay?.AllowFriendlyFire == true)
        {
            var survivor = _players.GetAllPlayers()
                .Where(player => player.Player.IsValid && player.Player.IsAlive)
                .Take(2)
                .ToList();

            if (survivor.Count == 1)
            {
                ShowTemporaryWinnerHud(
                    _core.Localizer["special_day_player_winner_hud", survivor[0].Player.Name, specialDay.Name],
                    _hudConfig.SpecialDayWinnerHud);
                return;
            }
        }

        switch (e.Winner)
        {
            case (byte)Team.CT:
                ShowTemporaryWinnerHud(_core.Localizer["guardians_win_round_hud"], _hudConfig.WinnerTeamHud, HudTextColor.Blue);
                break;
            case (byte)Team.T:
                ShowTemporaryWinnerHud(_core.Localizer["prisoners_win_round_hud"], _hudConfig.WinnerTeamHud, HudTextColor.Orange);
                break;
        }
    }

    private void ShowTemporaryWinnerHud(string text, HudTextSettings style, HudTextColor? colorOverride = null)
    {
        RemoveRoundWinnerHud();
        _roundWinnerHud = _hudText!.CreateHud(text, new HudTextOptions
        {
            Position = style.Position,
            Color = colorOverride ?? style.Color,
            Size = style.Size,
            Background = style.Background,
            BackgroundOpacity = style.BackgroundOpacity,
            DropShadow = style.DropShadow,
            OutlineColor = style.OutlineColor,
            Font = style.Font,
            FontWeight = style.FontWeight,
            TextAlignment = style.TextAlignment,
        });

    }

    private void RemoveRoundWinnerHud()
    {
        if (_hudText is not null && _roundWinnerHud is { } hud)
        {
            try
            {
                _hudText.RemoveHud(hud);
            }
            catch (ArgumentException)
            {
                // HudText discarded this handle during a map transition.
            }
        }

        _roundWinnerHud = null;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath e)
    {
        if (e.AttackerPlayer == null || e.UserIdPlayer == null)
            return HookResult.Continue;

        var attacker = _players.SyncPlayer(e.AttackerPlayer);
        var victim = _players.SyncPlayer(e.UserIdPlayer);

        if (attacker == null || victim == null)
            return HookResult.Continue;

        if (_specialDayManager.IsSpecialDayActive || _lastRequestManager.IsLastRequestActive)
            return HookResult.Continue;

        if (victim.IsWarden && attacker.Team == JBTeam.Prisoner)
        {
            _cuffsManager.OnWardenRemove(victim);
            victim.SetWarden(false, "killed", e.AttackerPlayer.Name);
            _soundManager.Play(JailbreakSound.WardenRemoved, JailbreakSoundReason.Killed);
            _wardenTagManager.RefreshNow();

            // Try to auto-assign a new warden after a short delay, same as round start.
            StartWardenCheckTimer();
        }

        return HookResult.Continue;
    }

    /// <summary>
    /// (Re)starts the delayed auto-warden-selection timer. Cancels any existing timer first.
    /// If a special day/last request is active or queued, does nothing (no timer is started).
    /// When it fires, it re-checks conditions and only picks from guards who are currently
    /// eligible to become warden (CanBecomeWarden), since state can change during the delay.
    /// </summary>
    public void StartWardenCheckTimer()
    {
        _wardenCheckCts?.Cancel();
        _wardenCheckCts = null;

        if (_specialDayManager.HasQueuedOrActiveSpecialDay || _lastRequestManager.IsLastRequestActive)
            return;

        _wardenCheckCts = _core.Scheduler.DelayBySeconds(_wardenConfig.AutoGiveWardenWhenNone, () =>
        {
            if (_specialDayManager.HasQueuedOrActiveSpecialDay || _lastRequestManager.IsLastRequestActive)
            {
                _wardenCheckCts?.Cancel();
                _wardenCheckCts = null;
                return;
            }
            if (_players.GetWarden() != null)
            {
                _wardenCheckCts?.Cancel();
                _wardenCheckCts = null;
                return;
            }

            var cts = _players.GetPlayersByTeam(JBTeam.Guard)
                .Where(p => p.CanBecomeWarden)
                .ToList();
            if (!cts.Any())
            {
                _wardenCheckCts?.Cancel();
                _wardenCheckCts = null;
                return;
            }

            var selected = cts[_random.Next(cts.Count)];
            selected.SetWarden(true);
            if (selected.IsWarden)
            {
                _wardenTagManager.RefreshNow();
                _soundManager.Play(JailbreakSound.WardenSet);
                _soundManager.PlayToPlayer(selected, JailbreakSound.YouWarden);
                _cuffsManager.OnWardenGive(selected);
                selected.SendMessage(MessageType.Chat, "you_are_new_warden", true);
            }

            _wardenCheckCts?.Cancel();
            _wardenCheckCts = null;
        });
    }

    private void ApplyTeamLoadout(IJBPlayer player)
    {
        PlayerUtils.Color(player.Player, new Color(255, 255, 255, 255), _core.Scheduler);

        player.CanBecomeWarden = !_isRoundEnding
            && !_specialDayManager.HasQueuedOrActiveSpecialDay
            && !_lastRequestManager.IsLastRequestActive
            && player.Team == JBTeam.Guard;

        var model = player.Team switch
        {
            JBTeam.Guard => PlayerUtils.PickRandomModel(_modelsConfig.GuardModels),
            JBTeam.Prisoner => PlayerUtils.PickRandomModel(_modelsConfig.PrisonerModels),
            _ => null
        };

        if (!string.IsNullOrEmpty(model))
            PlayerUtils.SetModel(player.Player, model, _core.Scheduler);

        if (_specialDayManager.HasQueuedOrActiveSpecialDay)
            return;

        if (player.Team == JBTeam.Prisoner)
            StripPrisonerWeapons(player);
        else if (player.Team == JBTeam.Guard)
            _guardGunsManager.GiveSavedLoadout(player);
    }

    private void StripPrisonerWeapons(IJBPlayer prisoner)
    {
        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (!prisoner.Player.IsValid || !prisoner.Player.IsAlive || prisoner.Team != JBTeam.Prisoner)
                return;

            StripWeapons(prisoner.Player);
            GiveWeapon(prisoner.Player, "weapon_knife");
        });
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
    private void StartCheckPrisonerVoiceTimer()
    {
        _checkPrisonersVoiceCts?.Cancel();
        _checkPrisonersVoiceCts = null;

        _checkPrisonersVoiceCts = _core.Scheduler.RepeatBySeconds(0.5f, () =>
        {
            foreach (var prisoner in _players.GetPlayersByTeam(JBTeam.Prisoner))
            {
                if (_core.Permission.PlayerHasPermissions(prisoner.SteamID, _voiceConfig.SkipVoicePenalties))
                    continue;

                if (prisoner.WasUnmutedByWarden)
                    continue;

                if (!prisoner.IsMuted)
                {
                    prisoner.Mute();
                }
            }
        });
    }
    private void StopCheckPrisonerVoiceTimer()
    {
        _checkPrisonersVoiceCts?.Cancel();
        _checkPrisonersVoiceCts = null;

        foreach (var prisoner in _players.GetPlayersByTeam(JBTeam.Prisoner))
        {
            if (_core.Permission.PlayerHasPermissions(prisoner.SteamID, _voiceConfig.SkipVoicePenalties))
                continue;

            if (prisoner.IsMuted)
            {
                prisoner.Unmute();
            }
        }
    }
}
