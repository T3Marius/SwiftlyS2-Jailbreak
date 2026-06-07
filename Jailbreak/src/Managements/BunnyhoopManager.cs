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
    private readonly SpecialDayManager   _specialDayManager;

    private CancellationTokenSource? _bhopCountdownCts;
    private Guid? _roundStartHookId;
    private bool _bunnyhoopEnabled;

    public BunnyhoopManager(ISwiftlyCore core, IOptions<UtilsConfig> utils, IJBPlayerManagement players, SpecialDayManager specialDayManager)
    {
        _core = core;
        _utils = utils.Value;
        _players = players;
        _specialDayManager = specialDayManager;
    }

    public void Register()
    {
        _roundStartHookId = _core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        _core.Event.OnTick += OnTick;
    }
    public void Unregister()
    {
        if (_roundStartHookId.HasValue)
        {
            _core.GameEvent.Unhook(_roundStartHookId.Value);
            _roundStartHookId = null;
        }

        _core.Event.OnTick -= OnTick;
        _bhopCountdownCts?.Cancel();
        _bhopCountdownCts = null;
    }
    private HookResult OnRoundStart(EventRoundStart e)
    {
        _bhopCountdownCts?.Cancel();
        _bhopCountdownCts = null;

        if (_specialDayManager.HasQueuedOrActiveSpecialDay)
        {
            EnableBunnyhoop(sendMessage: false);
            return HookResult.Continue;
        }

        DisableBunnyhoop();

        if (_utils.Bunnyhoop.RoundStartCountdown > 0)
        {
            _players.SendMessage(MessageType.Chat, "bunnyhoop_disabled", true, args: _utils.Bunnyhoop.RoundStartCountdown);
            _bhopCountdownCts = _core.Scheduler.DelayBySeconds(_utils.Bunnyhoop.RoundStartCountdown, () =>
            {
                EnableBunnyhoop(sendMessage: true);
                _bhopCountdownCts?.Cancel();
                _bhopCountdownCts = null;
            });
   
        }
        else
        {
            EnableBunnyhoop(sendMessage: false);
        }
        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (_specialDayManager.IsSpecialDayActive && !_bunnyhoopEnabled)
        {
            _bhopCountdownCts?.Cancel();
            _bhopCountdownCts = null;
            EnableBunnyhoop(sendMessage: false);
        }
    }

    private void EnableBunnyhoop(bool sendMessage)
    {
        SetBunnyhoop(true);

        if (sendMessage)
            _players.SendMessage(MessageType.Chat, "bunnyhoop_enabled", true);
    }

    private void DisableBunnyhoop()
    {
        SetBunnyhoop(false);
    }

    private void SetBunnyhoop(bool enabled)
    {
        _core.ConVar.Find<bool>("sv_autobunnyhopping")?.SetInternal(enabled);
        _core.ConVar.Find<bool>("sv_enablebunnyhopping")?.SetInternal(enabled);

        foreach (var player in _players.GetAllPlayers())
        {
            _core.ConVar.Find<bool>("sv_autobunnyhopping")?.ReplicateToClient(player.Player.PlayerID, enabled);
            _core.ConVar.Find<bool>("sv_enablebunnyhopping")?.ReplicateToClient(player.Player.PlayerID, enabled);
        }

        _bunnyhoopEnabled = enabled;
    }
}
