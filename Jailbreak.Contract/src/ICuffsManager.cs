namespace Jailbreak.Contract;

public interface ICuffsManager
{
    bool IsCuffed(IJBPlayer player);
    bool TryBreakCuffs(IJBPlayer prisoner);
}
