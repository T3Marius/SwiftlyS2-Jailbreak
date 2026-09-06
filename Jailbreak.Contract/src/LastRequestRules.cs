using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Players;

namespace Jailbreak.Contract;

public static class LastRequestRules
{
    // Bots share Steam IDs. A connection is identified by its slot and session.
    public static bool SamePlayer(IPlayer? left, IPlayer? right) =>
        left != null && right != null
        && left.PlayerID == right.PlayerID && left.SessionId == right.SessionId;

    public static bool SamePlayer(IJBPlayer? left, IJBPlayer? right) =>
        SamePlayer(left?.Player, right?.Player);

    public static bool IsParticipant(ILastRequest request, LastRequestStartContext context, IJBPlayer player) =>
        SamePlayer(player, context.Prisoner) || SamePlayer(player, context.Guard)
        || (request.OpponentMode == LastRequestOpponentMode.PrisonerVsAllGuards && player.Team == JBTeam.Guard);

    public static bool CanDamage(ILastRequest request, LastRequestStartContext context,
        IJBPlayer? victim, IJBPlayer? attacker, bool started)
    {
        var victimIn = victim != null && IsParticipant(request, context, victim);
        var attackerIn = attacker != null && IsParticipant(request, context, attacker);
        if (!victimIn && !attackerIn) return true;
        return started && victimIn && attackerIn && !SamePlayer(victim, attacker)
            && (SamePlayer(victim, context.Prisoner) || SamePlayer(attacker, context.Prisoner));
    }

    public static bool IsWeaponAllowed(ILastRequest request, LastRequestStartContext context, ItemDefinitionIndex weapon) =>
        context.SelectedWeapon is { } selected
            ? weapon == selected
            : request.AllowAllWeapons || request.AllowedWeapons.Contains(weapon);
}
