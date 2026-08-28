using HudText.Contract;

namespace Jailbreak;

public sealed class HudConfig
{
    public List<string> Options { get; set; } = ["hud", "center"];
    public string CurrentWardenAndDeputy { get; set; } = "hud";
    public HudTextSettings CurrentWardenAndDeputyHud { get; set; } = new();
}
public sealed class HudTextSettings
{
    public HudTextPosition Position { get; set; } = HudTextPosition.TopCenter;
    public bool Background { get; set; } = false;
    public bool DropShadow { get; set; } = true;
    public HudTextColor Color { get; set; } = HudTextColor.Olive;
    public HudTextSize Size { get; set; } = HudTextSize.Normal;
    public HudTextBackgroundOpacity BackgroundOpacity { get; set; } = HudTextBackgroundOpacity.Light;
    public HudTextColor OutlineColor { get; set; } = HudTextColor.White;
}