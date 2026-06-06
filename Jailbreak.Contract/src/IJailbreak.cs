namespace Jailbreak.Contract
{
    public interface IJailbreak
    {
        public static string Key => "JB_API";
        IJBPlayerManagement Players { get; }
        IReadOnlyCollection<ISpecialDay> SpecialDays { get; }
        ISpecialDay? CurrentSpecialDay { get; }
        ISpecialDay? QueuedSpecialDay { get; }
        int SpecialDayCooldownRoundsRemaining { get; }

        bool RegisterSpecialDay(ISpecialDay specialDay);
        bool UnregisterSpecialDay(string id);
        bool QueueSpecialDay(string id);
        bool StartSpecialDay(string id);
        void EndSpecialDay();
    }
}
