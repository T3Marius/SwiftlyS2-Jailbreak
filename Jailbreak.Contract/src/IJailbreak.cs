namespace Jailbreak.Contract
{
    public interface IJailbreak
    {
        public static string Key => "JB_API";
        IJBPlayerManagement Players { get; }
        IJBShop Shop { get; }
        IReadOnlyCollection<ISpecialDay> SpecialDays { get; }
        ISpecialDay? CurrentSpecialDay { get; }
        ISpecialDay? QueuedSpecialDay { get; }
        int SpecialDayCooldownRoundsRemaining { get; }
        IReadOnlyCollection<ILastRequest> LastRequests { get; }
        ILastRequest? CurrentLastRequest { get; }

        bool RegisterSpecialDay(ISpecialDay specialDay);
        bool UnregisterSpecialDay(string id);
        bool QueueSpecialDay(string id);
        bool StartSpecialDay(string id);
        void EndSpecialDay();
        bool RegisterLastRequest(ILastRequest lastRequest);
        bool UnregisterLastRequest(string id);
    }
}
