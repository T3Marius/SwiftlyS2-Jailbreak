using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace Jailbreak;

public sealed class NetMessages
{
    private readonly ISwiftlyCore        _core;
    private readonly IJBPlayerManagement _players;
    private readonly VoiceConfig         _voiceConfig;
    private readonly SpecialDayManager   _specialDayManager;

    private Guid? _voiceDataMsg;
    private CancellationTokenSource? _unmuteCts;

    public NetMessages(ISwiftlyCore core, IJBPlayerManagement players, IOptions<VoiceConfig> voiceConfig, SpecialDayManager specialDayManager)
    {
        _core = core;
        _players = players;
        _voiceConfig = voiceConfig.Value;
        _specialDayManager = specialDayManager;
    }

    public void Register()
    {
        _voiceDataMsg = _core.NetMessage.HookClientMessage<CCLCMsg_VoiceData>(OnVoiceData);
    }
    public void Unregister()
    {
        if (_voiceDataMsg.HasValue)
        {
            _core.NetMessage.Unhook(_voiceDataMsg.Value);
            _voiceDataMsg = null;
        }

        _unmuteCts?.Cancel();
        _unmuteCts = null;
    }
    private HookResult OnVoiceData(CCLCMsg_VoiceData msg, int playerId)
    {
        var sender = _core.PlayerManager.GetPlayer(playerId);
        if (sender == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(sender);
        if (player == null)
            return HookResult.Continue;

        if (_specialDayManager.IsSpecialDayActive)
            return HookResult.Continue;

        if (player.IsWarden)
        {
            // Mute the appropriate players while the warden is speaking.
            switch (_voiceConfig.MuteWhoWhenWardenSpeaks.ToLower())
            {
                case "both":
                    foreach (var p in _players.GetAllPlayers().Where(p => !p.IsWarden))
                    {
                        if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                            continue;
                        
                        p.Mute();
                    }
                    break;
                case "prisoners":
                    foreach (var p in _players.GetAllPlayers().Where(p => p.Team == JBTeam.Prisoner))
                    {
                        if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                            continue;

                        p.Mute();
                    }
                    break;
                case "guardians":
                    foreach (var p in _players.GetAllPlayers().Where(p => p.Team == JBTeam.Guard && !p.IsWarden))
                    {
                        if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                            continue;
                        
                        p.Mute();
                    }
                    break;
            }

            // Debounce: cancel any pending unmute and schedule a new one.
            // If no new voice packet arrives within 2s, unmute eligible players.
            _unmuteCts?.Cancel();
            _unmuteCts = _core.Scheduler.DelayBySeconds(_voiceConfig.WardenVoiceCheckIntervalSeconds, () =>
            {
                foreach (var p in _players.GetAllPlayers().Where(p => p.IsMuted))
                {
                    if (_voiceConfig.KeepPrisonersMutedDuringRound && p.Team == JBTeam.Prisoner)
                        continue;

                    if (_core.Permission.PlayerHasPermissions(p.SteamID, _voiceConfig.SkipVoicePenalties))
                        continue;

                    p.Unmute();
                }

                _unmuteCts = null;
            });
        }

        return HookResult.Continue;
    }
}
