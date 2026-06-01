namespace Jailbreak.Contract
{
    public interface IJailbreak
    {
        public static string Key => "JB_API";
        IJBPlayerManagement Players { get; }
    }
}