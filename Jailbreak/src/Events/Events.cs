using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class Events
{
    private readonly ISwiftlyCore        _core;
    private readonly IJBPlayerManagement _players;
    private readonly CellManager         _cellManager;
    private readonly BoxManager          _boxManager;

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
    private Random _random = new();

    public Events(
        ISwiftlyCore core, 
        IJBPlayerManagement playerManagement,
        CellManager cellManager,
        BoxManager boxManager,
        IOptions<WardenConfig> wardenConfig, 
        IOptions<ModelsConfig> modelsConfig, 
        IOptions<UtilsConfig> utilsConfig,
        IOptions<VoiceConfig> voiceConfig)
    {
        _core = core;
        _players = playerManagement;
        _cellManager = cellManager;
        _boxManager = boxManager;
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
        if (_playerSpawnHookId.HasValue)
        {
            _core.GameEvent.Unhook(_playerSpawnHookId.Value);
            _playerSpawnHookId = null;
        }

        if (_playerTeamChangeHookId.HasValue)
        {
            _core.GameEvent.Unhook(_playerTeamChangeHookId.Value);
            _playerTeamChangeHookId = null;
        }

        if (_playerDisconnectHookId.HasValue)
        {
            _core.GameEvent.Unhook(_playerDisconnectHookId.Value);
            _playerDisconnectHookId = null;
        }

        if (_roundStartHookId.HasValue)
        {
            _core.GameEvent.Unhook(_roundStartHookId.Value);
            _roundStartHookId = null;
        }

        if (_roundEndHookId.HasValue)
        {
            _core.GameEvent.Unhook(_roundEndHookId.Value);
            _roundEndHookId = null;
        }

        if (_playerDeathHookId.HasValue)
        {
            _core.GameEvent.Unhook(_playerDeathHookId.Value);
            _playerDeathHookId = null;
        }

        foreach (var cts in _centerTimers.Values)
            cts.Cancel();
        _centerTimers.Clear();
    }
    private HookResult OnPlayerSpawn(EventPlayerSpawn e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;
        
        var player = _players.GetOrCreatePlayer(e.UserIdPlayer);
        if (player == null)
            return HookResult.Continue;

        player.SyncTeam();

        if (player.Team == JBTeam.Guard)
        {
            player.CanBecomeWarden = true;
            var guardModel = PlayerUtils.PickRandomModel(_modelsConfig.GuardModels);
            if (!string.IsNullOrEmpty(guardModel))
                PlayerUtils.SetModel(player.Player, guardModel, _core.Scheduler);
        }
        else if (player.Team == JBTeam.Prisoner)
        {
            player.CanBecomeWarden = false;
            var prisonerModel = PlayerUtils.PickRandomModel(_modelsConfig.PrisonerModels);
            if (!string.IsNullOrEmpty(prisonerModel))
                PlayerUtils.SetModel(player.Player, prisonerModel, _core.Scheduler);
        }

        StartHudTimer(player);
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeamChange(EventPlayerTeam e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        var player = _players.GetOrCreatePlayer(e.UserIdPlayer);
        if (player == null)
            return HookResult.Continue;

        player.SyncTeam();
        if (player.Team == JBTeam.Guard)
        {
            player.CanBecomeWarden = true;
        }
        else if (player.Team == JBTeam.Prisoner)
        {
            player.CanBecomeWarden = false;
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        var steamId = e.UserIdPlayer.SteamID;
        StopHudTimer(steamId);
        _players.RemovePlayer(steamId);
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart e)
    {
        foreach (var p in _players.GetAllPlayers())
            p.WasUnmutedByWarden = false;

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
            }

            _core.Scheduler.DelayBySeconds(_voiceConfig.KeepPrisonersMutedForSecondsOnRoundStart, () =>
            {
                foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner).Where(p => p.IsMuted))
                {
                    if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                        continue;
                    
                    p.Unmute();
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
            currentWarden.SetWarden(false, silent: true);
        }

        foreach (var player in _players.GetAllPlayers())
        {
            player.CanBecomeWarden = true;
        }

        _wardenCheckCts = _core.Scheduler.DelayBySeconds(_wardenConfig.AutoGiveWardenWhenNone, () =>
        {
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
            selected.SendMessage(MessageType.Chat, "you_are_new_warden", true);

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

            _wardenCheckCts?.Cancel();
            _wardenCheckCts = null;
        });

        return HookResult.Continue;
    }
    
    private HookResult OnRoundEnd(EventRoundEnd e)
    {
        foreach (var p in _players.GetAllPlayers())
            p.WasUnmutedByWarden = false;

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
            currentWarden.SetWarden(false);
        }

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
    
        var attacker = _players.GetOrCreatePlayer(e.AttackerPlayer);
        var victim = _players.GetOrCreatePlayer(e.UserIdPlayer);

        if (attacker == null || victim == null)
            return HookResult.Continue;

        if (victim.IsWarden && attacker.Team == JBTeam.Prisoner)
        {
            victim.SetWarden(false, "killed", e.AttackerPlayer.Name);
        }

        return HookResult.Continue;
    }
    private void StartHudTimer(IJBPlayer player)
    {
        StopHudTimer(player.SteamID);

        var cts = _core.Scheduler.RepeatBySeconds(3f, () =>
        {
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