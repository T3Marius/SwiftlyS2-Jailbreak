using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class JBPlayerManagement : IJBPlayerManagement
{
    private readonly ISwiftlyCore            _core;
    private readonly IOptions<ModelsConfig>  _modelsConfig;
    private readonly IconManager             _iconManager;
    
    private readonly Dictionary<ulong, JBPlayer> _players = [];

    public JBPlayerManagement(ISwiftlyCore core, IOptions<ModelsConfig> modelsConfig, IconManager iconManager)
    {
        _core         = core;
        _modelsConfig = modelsConfig;
        _iconManager  = iconManager;
    }

    public IJBPlayer? GetOrCreatePlayer(IPlayer player)
    {
        if (!player.IsValid)
            return null;

        var playerKey = PlayerIdentity.GetKey(player);

        if (_players.TryGetValue(playerKey, out var jbPlayer))
        {
            jbPlayer.RefreshPlayer(player);
            return jbPlayer;
        }

        jbPlayer = new JBPlayer(player, _core, _modelsConfig, _iconManager);
        _players[playerKey] = jbPlayer;
        return jbPlayer;
    }

    public IJBPlayer? SyncPlayer(IPlayer player)
    {
        var jbPlayer = GetOrCreatePlayer(player);
        if (jbPlayer == null)
            return null;

        jbPlayer.SyncTeam();
        NormalizeTeamRole(jbPlayer);
        return jbPlayer;
    }

    public void RemovePlayer(ulong steamId)
    {
        foreach (var playerKey in _players
                     .Where(x => x.Value.SteamID == steamId)
                     .Select(x => x.Key)
                     .ToList())
        {
            _players.Remove(playerKey);
        }
    }

    public void RemovePlayer(IPlayer player)
    {
        _players.Remove(PlayerIdentity.GetKey(player));
    }

    public void SyncTeams()
    {
        var livePlayerKeys = new HashSet<ulong>();

        foreach (var rawPlayer in _core.PlayerManager.GetAllValidPlayers())
        {
            livePlayerKeys.Add(PlayerIdentity.GetKey(rawPlayer));

            var player = SyncPlayer(rawPlayer);
            if (player == null)
                continue;
        }

        foreach (var playerKey in _players.Keys.Where(playerKey => !livePlayerKeys.Contains(playerKey)).ToList())
            _players.Remove(playerKey);
    }

    public IEnumerable<IJBPlayer> GetAllPlayers()
    {
        SyncTeams();
        return _players.Values.ToList();
    }

    public IEnumerable<IJBPlayer> GetPlayersByRole(JBRole role)
    {
        SyncTeams();
        return _players.Values.Where(p => p.Role == role).ToList();
    }

    public IEnumerable<IJBPlayer> GetPlayersByTeam(JBTeam team)
    {
        SyncTeams();
        return _players.Values.Where(p => p.Team == team).ToList();
    }

    public IJBPlayer? GetWarden()
    {
        SyncTeams();
        return _players.Values.FirstOrDefault(p => p.IsWarden);
    }

    public IJBPlayer? GetDeputy()
    {
        SyncTeams();
        return _players.Values.FirstOrDefault(p => p.IsDeputy);
    }

    public void SendMessage(MessageType type, string key, bool prefix = true, int durationMs = 5000, params object[] args)
    {
        SyncTeams();

        foreach (var player in _players.Values.ToList())
            player.SendMessage(type, key, prefix, durationMs, args);
    }

    private static void NormalizeTeamRole(IJBPlayer player)
    {
        player.CanBecomeWarden = player.Team == JBTeam.Guard;

        if (player.Team == JBTeam.Guard)
            return;

        if (player.IsWarden)
            player.SetWarden(false);
        else if (player.IsDeputy)
            player.SetDeputy(false);
    }
}
