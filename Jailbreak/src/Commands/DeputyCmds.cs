using System.Windows.Input;
using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class DeputyCommands
{
    private readonly ISwiftlyCore        _core;
    private readonly DeputyConfig        _config;
    private readonly IJBPlayerManagement _players;
    private readonly SpecialDayManager   _specialDayManager;

    public DeputyCommands(ISwiftlyCore core, IOptions<DeputyConfig> config, IJBPlayerManagement players, SpecialDayManager specialDayManager)
    {
        _core = core;
        _players = players;
        _specialDayManager = specialDayManager;
        _config = config.Value;
    }

    public void Register()
    {
        foreach (var cmd in _config.Commands.BecomeDeputy)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, BecomeDeputy);
        }

        foreach (var cmd in _config.Commands.GiveUpDeputy)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, GiveUpDeputy);
        }

    }
    public void Unregister()
    {
        foreach (var cmd in _config.Commands.BecomeDeputy)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.GiveUpDeputy)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }
    }

    private void BecomeDeputy(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (player.Team != JBTeam.Guard)
        {
            player.SendMessage(MessageType.Chat, "deputy_not_guard");
            return;
        }

        var deputy = _players.GetDeputy();
        if (deputy != null)
        {
            player.SendMessage(MessageType.Chat, "deputy_already_exists");
            return;
        }

        var warden = _players.GetWarden();
        if (warden == null)
        {
            player.SendMessage(MessageType.Chat, "deputy_no_warden");
            return;
        }

        player.SetDeputy(true);
        if (player.IsDeputy)
        {
            player.SendMessage(MessageType.Chat, "you_are_new_deputy", true);
        }
    }

    private void GiveUpDeputy(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return; 

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsDeputy)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_deputy");
            return;
        }

        player.SetDeputy(false);
        _players.SendMessage(MessageType.Chat, "deputy_removed.giveup", args: player.Player.Name);
    }

    private bool BlockDuringSpecialDay(IJBPlayer player)
    {
        if (!_specialDayManager.IsSpecialDayActive)
            return false;

        player.SendMessage(MessageType.Chat, "special_day_active_blocked", true);
        return true;
    }
}
