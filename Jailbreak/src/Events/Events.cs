using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class Events
{
    private readonly ISwiftlyCore        _core;
    private readonly IJBPlayerManagement _players;
    private readonly CellManager         _cellManager;
    private readonly BoxManager          _boxManager;
    private readonly CuffsManager        _cuffsManager;
    private readonly DrawManager         _drawManager;
    private readonly SpecialDayManager   _specialDayManager;
    private readonly LastRequestManager  _lastRequestManager;
    private readonly GuardGunsManager    _guardGunsManager;
    private readonly WardenTagManager    _wardenTagManager;
    private readonly JailbreakSoundManager _soundManager;

    /* ------------------- Configs ------------------- */
    private readonly WardenConfig        _wardenConfig;
    private readonly ModelsConfig        _modelsConfig;
    private readonly UtilsConfig         _utilsConfig;
    private readonly VoiceConfig         _voiceConfig;
    /* ----------------------------------------------- */

    private readonly Dictionary<ulong, CancellationTokenSource> _centerTimers = [];

    /* -------------- Game Events -------------- */
    private Guid? _playerSpawnHookId;
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
        IOptions<VoiceConfig> voiceConfig)
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
    }
    
    public void Register()
    {
        _playerSpawnHookId = _core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        _playerTeamChangeHookId = _core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeamChange);
        _playerDisconnectHookId = _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        _roundStartHookId = _core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        _roundEndHookId = _core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        _playerDeathHookId = _core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
    }
    public void Unregister()
    {
        Unhook(ref _playerSpawnHookId);
        Unhook(ref _playerTeamChangeHookId);
        Unhook(ref _playerDisconnectHookId);
        Unhook(ref _roundStartHookId);
        Unhook(ref _roundEndHookId);
        Unhook(ref _playerDeathHookId);

        _wardenCheckCts?.Cancel();
        _wardenCheckCts = null;

        _doorsCheckCts?.Cancel();
        _doorsCheckCts = null;

        StopCheckPrisonerVoiceTimer();

        foreach (var cts in _centerTimers.Values)
            cts.Cancel();
        _centerTimers.Clear();
    }
    private HookResult OnPlayerSpawn(EventPlayerSpawn e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;
        
        var player = _players.SyncPlayer(e.UserIdPlayer);
        if (player == null)
            return HookResult.Continue;

        ApplyTeamLoadout(player);

        StartHudTimer(player);
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

        var steamId = e.UserIdPlayer.SteamID;
        StopHudTimer(steamId);
        _drawManager.CleanupPlayer(e.UserIdPlayer);
        _cuffsManager.CleanupPlayer(e.UserIdPlayer);
        _players.RemovePlayer(e.UserIdPlayer);
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart e)
    {
        _isRoundEnding = false;
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

        if (normalWardenBlocked)
            return HookResult.Continue;

        _wardenCheckCts = _core.Scheduler.DelayBySeconds(_wardenConfig.AutoGiveWardenWhenNone, () =>
        {
            if (_lastRequestManager.IsLastRequestActive)
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

            var cts = _players.GetPlayersByTeam(JBTeam.Guard).ToList();
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
            currentWarden.SetWarden(false);
            _wardenTagManager.RefreshNow();
        }

        _cuffsManager.CleanupAll();

        foreach (var player in _players.GetAllPlayers())
        {
            player.CanBecomeWarden = false;
        }

        return HookResult.Continue;
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
        }

        return HookResult.Continue;
    }
    private void StartHudTimer(IJBPlayer player)
    {
        StopHudTimer(player.SteamID);

        var cts = _core.Scheduler.RepeatBySeconds(3f, () =>
        {
            if (_specialDayManager.HasQueuedOrActiveSpecialDay || _lastRequestManager.IsLastRequestActive)
                return;

            var warden = _players.GetWarden()?.Player.Name ?? _core.Localizer["none"];
            var deputy = _players.GetDeputy()?.Player.Name ?? _core.Localizer["none"];
            player.SendMessage(MessageType.Center, "current_ct_roles.center", false, args: [warden, deputy]);
        });

        _centerTimers[player.SteamID] = cts;
    }

    private void StopHudTimer(ulong steamId)
    {
        if (_centerTimers.TryGetValue(steamId, out var cts))
        {
            cts.Cancel();
            _centerTimers.Remove(steamId);
        }
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
