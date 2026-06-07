namespace Jailbreak;

public sealed class PrisonerConfig
{
    public PrisonerCommandsConfig Commands { get; set; } = new();
}

public sealed class PrisonerCommandsConfig
{
    public List<string> LastRequest { get; set; } = ["lr"];
    public List<string> Surrender { get; set; } = ["s", "surrender"];
}
