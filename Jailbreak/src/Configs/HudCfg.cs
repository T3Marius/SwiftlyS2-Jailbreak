using HudText.Contract;

namespace Jailbreak;

public sealed class HudConfig
{
    public HudTextSettings CurrentWardenAndDeputyHud { get; set; } = new();
    public HudTextSettings CurrentDayDescriptionHud { get; set; } = new()
    {
        Position = HudTextPosition.CenterTop,
        TextAlignment = HudTextAlignment.Center,
        Background = false,
        DropShadow = true,
        Color = HudTextColor.Green,
        Font = HudTextFont.Stratum2,
        FontWeight = FontWeight.Bold,
        Size = HudTextSize.Small,
    };
    public HudTextSettings SpecialDayCountdownHud { get; set; } = new()
    {
        Position = HudTextPosition.CenterBottom,
        TextAlignment = HudTextAlignment.Center,
        Background = false,
        BackgroundOpacity = HudTextBackgroundOpacity.Light,
        DropShadow = true,
        Color = HudTextColor.Orange,
        OutlineColor = HudTextColor.Black,
        Font = HudTextFont.Stratum2,
        FontWeight = FontWeight.Bold,
        Size = HudTextSize.Normal,
    };
    public HudTextSettings CurrentLastRequestHud { get; set; } = new()
    {
        Position = HudTextPosition.CenterTop,
        TextAlignment = HudTextAlignment.Center,
        Background = false,
        DropShadow = true,
        Color = HudTextColor.Green,
        Font = HudTextFont.Stratum2,
        FontWeight = FontWeight.Bold,
        Size = HudTextSize.Small,
    };
    public HudTextSettings LastRequestCountdownHud { get; set; } = new()
    {
        Position = HudTextPosition.CenterBottom,
        TextAlignment = HudTextAlignment.Center,
        Background = false,
        BackgroundOpacity = HudTextBackgroundOpacity.Light,
        DropShadow = true,
        Color = HudTextColor.Orange,
        OutlineColor = HudTextColor.Black,
        Font = HudTextFont.Stratum2,
        FontWeight = FontWeight.Bold,
        Size = HudTextSize.Normal,
    };
    public HudTextSettings WinnerTeamHud { get; set; } = new()
    {
        Position = HudTextPosition.CenterMiddle,
        TextAlignment = HudTextAlignment.Center,
        Background = false,
        DropShadow = true,
        Font = HudTextFont.CourierNew,
        FontWeight = FontWeight.Bold,
        Size = HudTextSize.ExtraLarge,
    };
    public HudTextSettings SpecialDayWinnerHud { get; set; } = new()
    {
        Position = HudTextPosition.CenterMiddle,
        TextAlignment = HudTextAlignment.Center,
        Background = false,
        DropShadow = true,
        Color = HudTextColor.Yellow,
        Font = HudTextFont.CourierNew,
        FontWeight = FontWeight.Bold,
        Size = HudTextSize.ExtraLarge,
    };
    public HudTextSettings CurrencyHud { get; set; } = new()
    {
        Position = HudTextPosition.CenterLeft,
        VerticalAlignment = VerticalAlignment.Middle,
        MarginTop = 200,
        TextAlignment = HudTextAlignment.Left,
        Background = false,
        DropShadow = true,
        Color = HudTextColor.Yellow,
        Font = HudTextFont.Stratum2,
        FontWeight = FontWeight.Bold,
        Size = HudTextSize.Small,
    };
}
public sealed class HudTextSettings
{
    public HudTextPosition Position { get; set; } = HudTextPosition.CenterTop;
    public bool Background { get; set; } = false;
    public bool DropShadow { get; set; } = true;
    public HudTextColor Color { get; set; } = HudTextColor.Green;
    public HudTextSize Size { get; set; } = HudTextSize.Normal;
    /// <summary>Aligns the text inside the HUD panel.</summary>
    public HudTextAlignment TextAlignment { get; set; } = HudTextAlignment.Center;
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;
    public HudTextBackgroundOpacity BackgroundOpacity { get; set; } = HudTextBackgroundOpacity.Light;
    public HudTextColor OutlineColor { get; set; } = HudTextColor.White;
    public HudTextFont Font { get; set; } = HudTextFont.Stratum2;
    public FontWeight FontWeight { get; set; } = FontWeight.Bold;
    public int MarginBottom { get; set; }
    public int MarginTop { get; set; }
    public int MarginLeft { get; set; }
    public int MarginRight { get; set; }
}
