using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace Jailbreak.Contract
{
    /// <summary>
    /// Defines the various teams a player can belong to in the Jailbreak game mode.
    /// </summary>
    public enum JBTeam
    {
        None = 0,
        Prisoner = 1,
        Guard = 2,
    }

    /// <summary>
    /// Defines the various roles a player can have in the Jailbreak game mode.
    /// </summary>
    public enum JBRole
    {
        None = 0,
        Warden = 1,
        Deputy = 2,
        Rebel = 3,
        Freeday = 4
    }

    /// <summary>
    /// Represents a player in the Jailbreak game mode, encapsulating their role, team, and related properties.
    /// </summary>
    public interface IJBPlayer
    {
        IPlayer Player { get; }
        ulong SteamID { get; }

        JBTeam Team { get; set; }
        JBRole Role { get; set; }

        bool IsFreeday { get; }
        bool IsRebel { get; }
        bool IsDeputy { get; }
        bool IsWarden { get; }
        bool IsCuffed { get; set; }
        bool CanBecomeWarden { get; set; }

        ILocalizer Localizer { get; }

        /// <param name="killerName">Used for the 'killed' off-reason to fill the {1} placeholder in the translation.</param>
        void SetWarden(bool state, string? offReason = null, string? killerName = null, bool silent = false);
        void SetDeputy(bool state, string? offReason = null);
        void SetRebel(bool state, string? offReason = null);
        void SetFreeday(bool state, string? offReason = null);

        void SyncTeam();

        /// <param name="durationMs">How long HTML-overlay messages stay on screen, in milliseconds.</param>
        void SendMessage(MessageType type, string key, bool prefix = true, int durationMs = 5000, params object[] args);
    }
}