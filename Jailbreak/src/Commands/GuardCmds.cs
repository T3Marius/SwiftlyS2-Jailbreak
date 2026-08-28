using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class GuardCommands
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly GuardConfig _config;
    public GuardCommands(ISwiftlyCore core, IJBPlayerManagement players, IOptions<GuardConfig> config)
    {
        _core = core;
        _players = players;
        _config = config.Value;
    }
    public void Register()
    {
        foreach (var cmd in _config.Commands.LastGuard)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, Command_LastGuard);
        }
    }
    public void Unregister()
    {
        foreach (var cmd in _config.Commands.LastGuard)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }
    }
    private void Command_LastGuard(ICommandContext ctx)
    {
        if (ctx.Sender is not IPlayer sender)
            return;

        var guard = _players.GetOrCreatePlayer(sender);
        if (guard == null)
            return;

        var aliveGuards = _players.GetPlayersByTeam(JBTeam.Guard).Where(p => p.Player.IsAlive && !p.Player.IsFakeClient && p.SteamID != guard.SteamID).ToList();
        var alivePrisoners = _players.GetPlayersByTeam(JBTeam.Prisoner).Where(p => p.Player.IsAlive && !p.Player.IsFakeClient).ToList();

        if (aliveGuards.Count > 1)
        {
            guard.SendMessage(MessageType.Chat, "lg_not_alone");
            return;
        }

        if (alivePrisoners.Count <= _config.LastGuard.MinPrisoners)
        {
            guard.SendMessage(MessageType.Chat, "lg_min_prisoners", args: _config.LastGuard.MinPrisoners);
            return;
        }
    }
}