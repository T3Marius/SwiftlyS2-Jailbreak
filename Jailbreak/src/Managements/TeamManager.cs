using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class TeamManager
{
    private readonly ISwiftlyCore        _core;
    private readonly IJBPlayerManagement _players;
    private readonly UtilsConfig         _utilsConfig;
    private Guid? _clientCommandHookId;
    private Guid? _playerTeamHookId;

    public TeamManager(ISwiftlyCore core, IJBPlayerManagement players, IOptions<UtilsConfig> utilsConfig)
    {
        _core        = core;
        _players     = players;
        _utilsConfig = utilsConfig.Value;
    }

    public void Register()
    {
        _clientCommandHookId = _core.Command.HookClientCommand(OnClientCommand);
        _playerTeamHookId = _core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeam);
    }

    public void Unregister()
    {
        if (_clientCommandHookId.HasValue)
        {
            _core.Command.UnhookClientCommand(_clientCommandHookId.Value);
            _clientCommandHookId = null;
        }

        Unhook(ref _playerTeamHookId);
    }

    private HookResult OnClientCommand(int playerId, string commandLine)
    {
        if (!IsJoinGuardCommand(commandLine))
            return HookResult.Continue;

        var rawPlayer = _core.PlayerManager.GetPlayer(playerId);
        if (rawPlayer == null)
            return HookResult.Continue;

        if (!CanJoinGuard(rawPlayer))
        {
            var player = _players.SyncPlayer(rawPlayer);
            player?.SendMessage(MessageType.Chat, "team_ratio_full", args: [PrisonersPerGuard]);
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam e)
    {
        var player = e.UserIdPlayer;
        if (player == null || e.Disconnect || e.IsBot || e.Team != (byte)Team.CT)
            return HookResult.Continue;

        var playerId = player.PlayerID;

        _core.Scheduler.NextWorldUpdate(() =>
        {
            var currentPlayer = _core.PlayerManager.GetPlayer(playerId);
            if (currentPlayer == null || !currentPlayer.IsValid)
                return;

            if (GetTeam(currentPlayer) != Team.CT || IsCurrentGuardRatioAllowed())
                return;

            currentPlayer.SwitchTeam(Team.T);

            var jbPlayer = _players.SyncPlayer(currentPlayer);
            jbPlayer?.SendMessage(MessageType.Chat, "team_ratio_full", args: [PrisonersPerGuard]);
        });

        return HookResult.Continue;
    }

    public bool CanJoinGuard(IPlayer player)
    {
        var counts = CountProjectedTeams(player, Team.CT);
        return IsGuardRatioAllowed(counts.Guards, counts.Prisoners);
    }

    public bool MoveToGuard(IJBPlayer player)
    {
        if (!player.Player.IsValid || !CanJoinGuard(player.Player))
            return false;

        player.Player.SwitchTeam(Team.CT);
        return true;
    }

    public int PrisonersPerGuard => Math.Max(1, _utilsConfig.PrisonerPerGuardRatio);

    private bool IsGuardRatioAllowed(int guards, int prisoners)
    {
        var totalActive = guards + prisoners;
        if (totalActive <= 1)
            return true;

        return guards * PrisonersPerGuard <= prisoners;
    }

    private bool IsCurrentGuardRatioAllowed()
    {
        var counts = CountCurrentTeams();
        return IsGuardRatioAllowed(counts.Guards, counts.Prisoners);
    }

    private (int Guards, int Prisoners) CountProjectedTeams(IPlayer movingPlayer, Team targetTeam)
    {
        var guards = 0;
        var prisoners = 0;

        foreach (var player in _core.PlayerManager.GetAllValidPlayers())
        {
            var team = player.PlayerID == movingPlayer.PlayerID ? targetTeam : GetTeam(player);
            CountTeam(team, ref guards, ref prisoners);
        }

        return (guards, prisoners);
    }

    private (int Guards, int Prisoners) CountCurrentTeams()
    {
        var guards = 0;
        var prisoners = 0;

        foreach (var player in _core.PlayerManager.GetAllValidPlayers())
            CountTeam(GetTeam(player), ref guards, ref prisoners);

        return (guards, prisoners);
    }

    private static void CountTeam(Team team, ref int guards, ref int prisoners)
    {
        if (team == Team.CT)
            guards++;
        else if (team == Team.T)
            prisoners++;
    }

    private static Team GetTeam(IPlayer player)
    {
        return (Team)player.Controller.TeamNum;
    }

    private static bool IsJoinGuardCommand(string commandLine)
    {
        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
            && parts[0].Equals("jointeam", StringComparison.OrdinalIgnoreCase)
            && parts[1] == "3";
    }

    private void Unhook(ref Guid? hookId)
    {
        if (!hookId.HasValue)
            return;

        _core.GameEvent.Unhook(hookId.Value);
        hookId = null;
    }
}
