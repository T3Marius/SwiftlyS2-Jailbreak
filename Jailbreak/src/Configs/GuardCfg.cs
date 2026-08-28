namespace Jailbreak;

public sealed class GuardConfig
{
    public GuardCommandsCfg Commands { get; set; } = new();
    public LastGuardCfg LastGuard { get; set; } = new();
}
public sealed class GuardCommandsCfg
{
    public List<string> GuardGuns { get; set; } = ["guns"];
    public List<string> LastGuard { get; set; } = ["lg", "lastguard"];
}
public sealed class LastGuardCfg
{
    public int MinPrisoners { get; set; } = 3;
    public int RoundDelay { get; set; } = 2;

    public int GuardHealth { get; set; } = 500;
}