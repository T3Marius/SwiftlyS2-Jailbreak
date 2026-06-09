namespace Jailbreak;

public sealed class WardenConfig
{
    public WardenCommandsCfg Commands { get; set; } = new();
    public WardenTagCfg Tag { get; set; } = new();
    public int AutoGiveWardenWhenNone { get; set; } = 10;
}

public sealed class WardenTagCfg
{
    public bool Enable { get; set; } = true;
    public string Chat { get; set; } = "[gold]★[default] [blue]Warden[default] ";
    public string NameColor { get; set; } = "[red]";
    public string Scoreboard { get; set; } = "★ Warden";
    public float ScoreboardRefreshSeconds { get; set; } = 0.5f;
}

public sealed class WardenCommandsCfg
{
    public List<string> BecomeWarden { get; set; } = ["w", "warden"];
    public List<string> GiveUpWarden { get; set; } = ["unwarden", "uw"];
    public List<string> WardenHelp { get; set; } = ["whelp", "wh"];
    public List<string> WardenMenu { get; set; } = ["wmenu", "wm"];
    public List<string> SpecialDays { get; set; } = ["sd"];
    public List<string> ToggleBox { get; set; } = ["box"];
    public List<string> ToggleCells { get; set; } = ["cells", "c"];
    public List<string> ToggleDraw { get; set; } = ["draw"];
    public List<string> DrawColor { get; set; } = ["drawcolor"];
    public List<string> DrawClear { get; set; } = ["drawclear"];
    public List<string> GuardGuns { get; set; } = ["guns"];
}
