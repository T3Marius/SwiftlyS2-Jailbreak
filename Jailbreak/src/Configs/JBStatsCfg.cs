namespace Jailbreak;

public sealed class JBStatsConfig
{
    public List<string> Commands { get; set; } = ["jbstats", "jstats", "stats"];
    public int TopLimit { get; set; } = 10;
}
