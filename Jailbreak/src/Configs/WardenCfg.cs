namespace Jailbreak;

public sealed class WardenConfig
{
    public WardenCommandsCfg Commands { get; set; } = new();
    public int AutoGiveWardenWhenNone { get; set; } = 10;
}
public sealed class WardenCommandsCfg
{
    public List<string> BecomeWarden { get; set; } = ["w", "warden"];
    public List<string> GiveUpWarden { get; set; } = ["unwarden", "uw"];
    public List<string> WardenHelp { get; set; } = ["whelp", "wh"];
    public List<string> WardenMenu { get; set; } = ["wmenu", "wm"];
    public List<string> ToggleBox { get; set; } = ["box"];
    public List<string> ToggleCells { get; set; } = ["cells", "c"];
}
