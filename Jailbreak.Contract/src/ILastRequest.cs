using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;

namespace Jailbreak.Contract
{
    public enum LastRequestOpponentMode
    {
        Duel = 0,
        PrisonerVsAllGuards = 1
    }

    public enum LastRequestWeaponSelection
    {
        None = 0,
        Optional = 1,
        Required = 2
    }

    public enum LastRequestEndReason
    {
        Completed = 0,
        Cancelled = 1,
        PrisonerDisconnected = 2,
        GuardDisconnected = 3,
        PrisonerDied = 4,
        GuardDied = 5,
        Unexpected = 6
    }

    public sealed record LastRequestVariant(string Id, string Name, string Description = "");

    public sealed record LastRequestStartContext(
        IJBPlayer Prisoner,
        IJBPlayer? Guard,
        ItemDefinitionIndex? SelectedWeapon,
        LastRequestVariant? SelectedVariant);

    public interface ILastRequest
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        int StartCountdown { get; }

        IJBPlayer Prisoner { get; }
        IJBPlayer? Guard { get; }
        ItemDefinitionIndex? SelectedWeapon { get; }
        LastRequestVariant? SelectedVariant { get; }
        LastRequestEndReason LastEndReason { get; }

        LastRequestOpponentMode OpponentMode { get; }
        LastRequestWeaponSelection WeaponSelection { get; }
        bool AllowAllWeapons { get; }
        IReadOnlySet<ItemDefinitionIndex> AllowedWeapons { get; }
        IReadOnlyList<ItemDefinitionIndex> WeaponMenuWeapons { get; }
        IReadOnlyList<LastRequestVariant> Variants { get; }
        bool StripWeaponsOnStart { get; }
        IReadOnlyList<string> GiveWeaponsOnStart { get; }

        bool RequiresGuardSelection { get; }
        bool RequiresWeaponSelection { get; }
        bool RequiresVariantSelection { get; }

        bool CanStart(LastRequestStartContext context);
        void Start(LastRequestStartContext context);
        void End(IJBPlayer? winner, IJBPlayer? loser);
        void OnPlayerDisconnected(IJBPlayer player);
        void OnPlayerDied(IJBPlayer victim, IJBPlayer? attacker);
    }

    public abstract class LastRequestBase : ILastRequest
    {
        private IJBPlayer? _prisoner;

        protected ISwiftlyCore Core { get; }
        protected IJailbreak Jailbreak { get; }

        protected LastRequestBase(ISwiftlyCore core, IJailbreak jailbreak)
        {
            Core = core;
            Jailbreak = jailbreak;
        }

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual int StartCountdown => 5;

        public IJBPlayer Prisoner => _prisoner ?? throw new InvalidOperationException("Last Request has not been started yet.");
        public IJBPlayer? Guard { get; private set; }
        public ItemDefinitionIndex? SelectedWeapon { get; private set; }
        public LastRequestVariant? SelectedVariant { get; private set; }
        public LastRequestEndReason LastEndReason { get; private set; } = LastRequestEndReason.Completed;

        public virtual LastRequestOpponentMode OpponentMode => LastRequestOpponentMode.Duel;
        public virtual LastRequestWeaponSelection WeaponSelection => LastRequestWeaponSelection.None;
        public virtual bool AllowAllWeapons => false;
        public virtual IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => new HashSet<ItemDefinitionIndex>();
        public virtual IReadOnlyList<ItemDefinitionIndex> WeaponMenuWeapons => AllowAllWeapons
            ? LastRequestWeapons.GunsMenuWeapons.ToArray()
            : AllowedWeapons.ToArray();
        public virtual IReadOnlyList<LastRequestVariant> Variants => [];
        public virtual bool StripWeaponsOnStart => true;
        public virtual IReadOnlyList<string> GiveWeaponsOnStart => [];

        public virtual bool RequiresGuardSelection => OpponentMode == LastRequestOpponentMode.Duel;
        public virtual bool RequiresWeaponSelection => WeaponSelection == LastRequestWeaponSelection.Required;
        public virtual bool RequiresVariantSelection => Variants.Count > 0;

        public virtual bool CanStart(LastRequestStartContext context)
        {
            if (context.Prisoner.Team != JBTeam.Prisoner || context.Prisoner.IsRebel)
                return false;

            if (RequiresGuardSelection && context.Guard == null)
                return false;

            if (context.Guard != null && context.Guard.Team != JBTeam.Guard)
                return false;

            if (RequiresWeaponSelection && context.SelectedWeapon == null)
                return false;

            if (context.SelectedWeapon.HasValue && !AllowAllWeapons && !AllowedWeapons.Contains(context.SelectedWeapon.Value))
                return false;

            if (RequiresVariantSelection && context.SelectedVariant == null)
                return false;

            if (context.SelectedVariant != null && Variants.All(variant => variant.Id != context.SelectedVariant.Id))
                return false;

            return true;
        }

        public virtual void Start(LastRequestStartContext context)
        {
            _prisoner = context.Prisoner;
            Guard = context.Guard;
            SelectedWeapon = context.SelectedWeapon;
            SelectedVariant = context.SelectedVariant;
            LastEndReason = LastRequestEndReason.Completed;
        }

        public virtual void End(IJBPlayer? winner, IJBPlayer? loser)
        {
            _prisoner = null;
            Guard = null;
            SelectedWeapon = null;
            SelectedVariant = null;
        }

        public virtual void OnPlayerDisconnected(IJBPlayer player)
        {
            if (_prisoner != null && player.SteamID == _prisoner.SteamID)
            {
                LastEndReason = LastRequestEndReason.PrisonerDisconnected;
                End(Guard, _prisoner);
                return;
            }

            if (Guard != null && player.SteamID == Guard.SteamID)
            {
                LastEndReason = LastRequestEndReason.GuardDisconnected;
                End(_prisoner, Guard);
            }
        }

        public virtual void OnPlayerDied(IJBPlayer victim, IJBPlayer? attacker)
        {
            if (_prisoner != null && victim.SteamID == _prisoner.SteamID)
            {
                LastEndReason = LastRequestEndReason.PrisonerDied;
                End(attacker ?? Guard, _prisoner);
                return;
            }

            if (Guard != null && victim.SteamID == Guard.SteamID)
            {
                LastEndReason = LastRequestEndReason.GuardDied;
                End(_prisoner, Guard);
            }
        }

        protected void MarkCancelled()
        {
            LastEndReason = LastRequestEndReason.Cancelled;
        }

        protected void MarkUnexpected()
        {
            LastEndReason = LastRequestEndReason.Unexpected;
        }
    }
}
