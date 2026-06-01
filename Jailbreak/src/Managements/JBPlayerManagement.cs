using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class JBPlayerManagement : IJBPlayerManagement
{
    private readonly ISwiftlyCore           _core;
    private readonly IOptions<IconsConfig>  _iconsConfig;
    private readonly IOptions<ModelsConfig> _modelsConfig;
    private readonly IconManager            _icons;
    
    private readonly Dictionary<ulong, JBPlayer> _players = [];

    public JBPlayerManagement(ISwiftlyCore core, IOptions<IconsConfig> iconsConfig, IOptions<ModelsConfig> modelsConfig, IconManager icons)
    {
        _core         = core;
        _iconsConfig  = iconsConfig;
        _modelsConfig = modelsConfig;
        _icons        = icons;
    }

    public IJBPlayer? GetOrCreatePlayer(IPlayer player)
    {
        if (_players.TryGetValue(player.SteamID, out var jbPlayer))
            return jbPlayer;

        jbPlayer = new JBPlayer(player, _core, _modelsConfig, _iconsConfig, _icons);
        _players[player.SteamID] = jbPlayer;
        return jbPlayer;
    }

    public void RemovePlayer(ulong steamId)
    {
        _players.Remove(steamId);
    }

    public void SyncTeams()
    {
        _players.Clear();

        foreach (var rawPlayer in _core.PlayerManager.GetAllValidPlayers())
        {
            var player = GetOrCreatePlayer(rawPlayer);
            if (player == null)
                continue;

            player.SyncTeam();
        }
    }

    public IEnumerable<IJBPlayer> GetAllPlayers()               => _players.Values;
    public IEnumerable<IJBPlayer> GetPlayersByRole(JBRole role) => _players.Values.Where(p => p.Role == role);
    public IEnumerable<IJBPlayer> GetPlayersByTeam(JBTeam team) => _players.Values.Where(p => p.Team == team);

    public IJBPlayer? GetWarden()  => _players.Values.FirstOrDefault(p => p.IsWarden);
    public IJBPlayer? GetDeputy()  => _players.Values.FirstOrDefault(p => p.IsDeputy);

    public void SendMessage(MessageType type, string key, bool prefix = true, int durationMs = 5000, params object[] args)
    {
        foreach (var player in _players.Values)
            player.SendMessage(type, key, prefix, durationMs, args);
    }
}
