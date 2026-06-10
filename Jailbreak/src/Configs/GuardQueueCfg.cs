namespace Jailbreak;

public sealed class GuardQueueConfig
{
    public bool Enable { get; set; } = true;
    public GuardQueueCommandsConfig Commands { get; set; } = new();
    public List<string> PremiumPermissions { get; set; } = ["jb.queue.premium", "jb.premium"];
}

public sealed class GuardQueueCommandsConfig
{
    public List<string> Queue { get; set; } = ["q", "queue"];
    public List<string> Unqueue { get; set; } = ["uq", "unqueue"];
}
