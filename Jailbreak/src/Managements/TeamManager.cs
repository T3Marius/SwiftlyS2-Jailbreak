using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class TeamManager
{
    private readonly ISwiftlyCore        _core;
    private readonly IJBPlayerManagement _players;
    private readonly UtilsConfig         _utilsConfig;

    public TeamManager(ISwiftlyCore core, IJBPlayerManagement players, IOptions<UtilsConfig> utilsConfig)
    {
        _core        = core;
        _players     = players;
        _utilsConfig = utilsConfig.Value;
    }

    [ClientCommandHookHandler]
    private HookResult OnClientCommand(int playerId, string commandLine)
    {
        if (!commandLine.Equals("jointeam 3", StringComparison.OrdinalIgnoreCase))
            return HookResult.Continue;

        var rawPlayer = _core.PlayerManager.GetPlayer(playerId);
        if (rawPlayer == null)
            return HookResult.Continue;

        var currentGuards    = _players.GetPlayersByTeam(JBTeam.Guard).Count();
        var currentPrisoners = _players.GetPlayersByTeam(JBTeam.Prisoner).Count();
        var totalActive      = currentGuards + currentPrisoners;

        if (totalActive < 2)
            return HookResult.Continue; // Only 1 player on the server, allow free team switching.

        // Always allow at least 1 guard slot; ratio only enforced once there are enough players.
        var maxGuards = Math.Max(1, totalActive / (_utilsConfig.PrisonerPerGuardRatio + 1));

        if (currentGuards >= maxGuards)
        {
            var player = _players.SyncPlayer(rawPlayer);
            player?.SendMessage(MessageType.Chat, "team_ratio_full", args: [_utilsConfig.PrisonerPerGuardRatio]);
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }
}
