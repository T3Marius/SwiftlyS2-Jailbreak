using SwiftlyS2.Shared.Players;

namespace Jailbreak.Contract
{
    public interface IJBPlayerManagement
    {
        IJBPlayer? GetOrCreatePlayer(IPlayer player);
        void RemovePlayer(ulong steamId);
        IJBPlayer? GetWarden();
        IJBPlayer? GetDeputy();

        IEnumerable<IJBPlayer> GetAllPlayers();
        IEnumerable<IJBPlayer> GetPlayersByRole(JBRole role);
        IEnumerable<IJBPlayer> GetPlayersByTeam(JBTeam team);

        void SyncTeams();

        /// <param name="durationMs">How long HTML-overlay messages stay on screen, in milliseconds.</param>
        void SendMessage(MessageType type, string key, bool prefix = true, int durationMs = 5000, params object[] args);
    }
}