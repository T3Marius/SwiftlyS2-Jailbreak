using Jailbreak.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using Microsoft.Extensions.Options;

namespace Jailbreak;

public sealed class JBStatsCommands
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly JBStats _menu;
    private readonly JBStatsConfig _config;

    public JBStatsCommands(ISwiftlyCore core, IJBPlayerManagement players, JBStats menu, IOptions<JBStatsConfig> config)
    {
        _core = core;
        _players = players;
        _menu = menu;
        _config = config.Value;
    }

    public void Register()
    {
        foreach (var command in _config.Commands)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, StatsCommand);
        }
    }

    public void Unregister()
    {
        foreach (var command in _config.Commands)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }
    }

    private void StatsCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        _menu.Show(player);
    }
}
