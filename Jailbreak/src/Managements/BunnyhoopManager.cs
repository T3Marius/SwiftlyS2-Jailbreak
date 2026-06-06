using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class BunnyhoopManager
{
    private readonly ISwiftlyCore        _core;
    private readonly UtilsConfig         _utils;
    private readonly IJBPlayerManagement _players;

    private CancellationTokenSource? _bhopCountdownCts;
    private Guid? _roundStartHookId;

    public BunnyhoopManager(ISwiftlyCore core, IOptions<UtilsConfig> utils, IJBPlayerManagement players)
    {
        _core = core;
        _utils = utils.Value;
        _players = players;
    }

    public void Register()
    {
        _roundStartHookId = _core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
    }
    public void Unregister()
    {
        if (_roundStartHookId.HasValue)
        {
            _core.GameEvent.Unhook(_roundStartHookId.Value);
        }
    }
    private HookResult OnRoundStart(EventRoundStart e)
    {
        _bhopCountdownCts?.Cancel();
        _bhopCountdownCts = null;

        _core.ConVar.Find<bool>("sv_autobunnyhopping")?.SetInternal(false);
        _core.ConVar.Find<bool>("sv_enablebunnyhopping")?.SetInternal(false);        

        if (_utils.Bunnyhoop.RoundStartCountdown > 0)
        {
            _players.SendMessage(MessageType.Chat, "bunnyhoop_disabled", true, args: _utils.Bunnyhoop.RoundStartCountdown);
            _bhopCountdownCts = _core.Scheduler.DelayBySeconds(_utils.Bunnyhoop.RoundStartCountdown, () =>
            {
                _core.ConVar.Find<bool>("sv_autobunnyhopping")?.SetInternal(true);
                _core.ConVar.Find<bool>("sv_enablebunnyhopping")?.SetInternal(true);   

                _players.SendMessage(MessageType.Chat, "bunnyhoop_enabled", true);
                _bhopCountdownCts?.Cancel();
                _bhopCountdownCts = null;
            });
   
        }
        else
        {
            _core.ConVar.Find<bool>("sv_autobunnyhopping")?.SetInternal(true);
            _core.ConVar.Find<bool>("sv_enablebunnyhopping")?.SetInternal(true);        
        }
        return HookResult.Continue;
    }
}