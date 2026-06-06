using Jailbreak.Contract;

namespace Jailbreak;

public sealed class Api : IJailbreak
{
    public IJBPlayerManagement Players { get; }
    public IReadOnlyCollection<ISpecialDay> SpecialDays => _specialDayManager.SpecialDays;
    public ISpecialDay? CurrentSpecialDay => _specialDayManager.CurrentSpecialDay;
    public ISpecialDay? QueuedSpecialDay => _specialDayManager.QueuedSpecialDay;
    public int SpecialDayCooldownRoundsRemaining => _specialDayManager.CooldownRoundsRemaining;

    private readonly SpecialDayManager _specialDayManager;

    public Api(IJBPlayerManagement playerManagement, SpecialDayManager specialDayManager)
    {
        Players = playerManagement;
        _specialDayManager = specialDayManager;
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
}
