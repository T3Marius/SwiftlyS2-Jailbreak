namespace Jailbreak;

public sealed class DeputyConfig
{
    public DeputyCommandsCfg Commands { get; set; } = new();
}
public sealed class DeputyCommandsCfg
{
    public List<string> BecomeDeputy { get; set; } = ["d", "deputy"];
    public List<string> GiveUpDeputy { get; set; } = ["undeputy", "ud"];
}