using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared;

namespace Jailbreak.Contract
{
    public enum SpecialDayFreezeTeam
    {
        None = 0,
        All = 1,
        Prisoners = 2,
        Guards = 3
    }

    public interface ISpecialDay
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        int StartCountdown { get; }
        bool AllowAllWeapons { get; }
        IReadOnlySet<ItemDefinitionIndex> AllowedWeapons { get; }
        bool EnableGunsMenu { get; }
        IReadOnlyList<ItemDefinitionIndex> GunsMenuWeapons { get; }
        bool StripWeaponsOnStart { get; }
        IReadOnlyList<string> GiveWeaponsOnStart { get; }
        SpecialDayFreezeTeam FreezeTeamOnCountdown { get; }
        bool AllowFriendlyFire { get; }

        bool CanStart();
        void OnCountdownTick(int secondsRemaining);
        void Start();
        void End();
    }

    public abstract class SpecialDayBase : ISpecialDay
    {
        protected ISwiftlyCore Core { get; }
        protected IJailbreak Jailbreak { get; }

        protected SpecialDayBase(ISwiftlyCore core, IJailbreak jailbreak)
        {
            Core = core;
            Jailbreak = jailbreak;
        }

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual int StartCountdown => 0;
        public virtual bool AllowAllWeapons => false;
        public virtual IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => new HashSet<ItemDefinitionIndex>();
        public virtual bool EnableGunsMenu => AllowAllWeapons || GunsMenuWeapons.Count > 0;
        public virtual IReadOnlyList<ItemDefinitionIndex> GunsMenuWeapons => AllowAllWeapons
            ? SpecialDayWeapons.GunsMenuWeapons.ToArray()
            : AllowedWeapons.ToArray();
        public virtual bool StripWeaponsOnStart => true;
        public virtual IReadOnlyList<string> GiveWeaponsOnStart => [];
        public virtual SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.All;
        public virtual bool AllowFriendlyFire => false;

        public virtual bool CanStart()
        {
            return true;
        }

        public virtual void OnCountdownTick(int secondsRemaining)
        {
        }

        public abstract void Start();
        public abstract void End();
    }
}
