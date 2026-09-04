using Jailbreak.Contract;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public static class PlayerIdentity
{
    private const ulong SessionKeyMask = 1UL << 63;

    public static ulong GetKey(IPlayer player)
    {
        return UsesSteamKey(player)
            ? player.SteamID
            : SessionKeyMask | player.SessionId;
    }

    public static bool UsesSteamKey(IPlayer player)
    {
        return player.SteamID != 0 && !player.IsFakeClient;
    }

    internal static IJBPlayer? FindByKey(this IJBPlayerManagement players, ulong key)
    {
        return players is JBPlayerManagement tracked
            ? tracked.FindByKey(key)
            : players.GetAllPlayers().FirstOrDefault(player => GetKey(player.Player) == key);
    }
}
