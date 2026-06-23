using Jailbreak.Contract;

namespace Jailbreak;

public sealed class Api : IJailbreak
{
    public IJBPlayerManagement Players { get; }
    public IJBShop Shop { get; }
    public ICuffsManager Cuffs { get; }
    public IReadOnlyCollection<ISpecialDay> SpecialDays => _specialDayManager.SpecialDays;
    public ISpecialDay? CurrentSpecialDay => _specialDayManager.CurrentSpecialDay;
    public ISpecialDay? QueuedSpecialDay => _specialDayManager.QueuedSpecialDay;
    public int SpecialDayCooldownRoundsRemaining => _specialDayManager.CooldownRoundsRemaining;
    public IReadOnlyCollection<ILastRequest> LastRequests => _lastRequestManager.LastRequests;
    public ILastRequest? CurrentLastRequest => _lastRequestManager.CurrentLastRequest;

    private readonly SpecialDayManager _specialDayManager;
    private readonly LastRequestManager _lastRequestManager;

    public Api(
        IJBPlayerManagement playerManagement,
        IJBShop shop,
        ICuffsManager cuffsManager,
        SpecialDayManager specialDayManager,
        LastRequestManager lastRequestManager)
    {
        Players = playerManagement;
        Shop = shop;
        Cuffs = cuffsManager;
        _specialDayManager = specialDayManager;
        _lastRequestManager = lastRequestManager;
    }

    public bool RegisterSpecialDay(ISpecialDay specialDay)
    {
        return _specialDayManager.RegisterSpecialDay(specialDay);
    }

    public bool UnregisterSpecialDay(string id)
    {
        return _specialDayManager.UnregisterSpecialDay(id);
    }

    public bool QueueSpecialDay(string id)
    {
        return _specialDayManager.QueueSpecialDay(id);
    }

    public bool StartSpecialDay(string id)
    {
        return _specialDayManager.StartSpecialDay(id);
    }

    public void EndSpecialDay()
    {
        _specialDayManager.EndSpecialDay();
    }

    public bool RegisterLastRequest(ILastRequest lastRequest)
    {
        return _lastRequestManager.RegisterLastRequest(lastRequest);
    }

    public bool UnregisterLastRequest(string id)
    {
        return _lastRequestManager.UnregisterLastRequest(id);
    }
}
