namespace SpecialDays;

public sealed class SDConfig
{
    public KnifeFightConfig  KnifeFight  { get; set; }    = new();
    public FreeForAllConfig  FreeForAll  { get; set; }    = new();
    public TeleportConfig    Teleport    { get; set; }    = new();
    public HideAndSeekConfig HideAndSeek { get; set; }    = new();
    public WarConfig         War         { get; set; }    = new();
    public NoScopeConfig     NoScope     { get; set; }    = new();
    public ScoutConfig       Scout       { get; set; }    = new();
    public TaserConfig       Taser       { get; set; }    = new();
}
public sealed class KnifeFightConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 10;
}
public sealed class FreeForAllConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 15;
}
public sealed class TeleportConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 15;
}
public sealed class HideAndSeekConfig
{
    public bool Enabled { get; set; } = true;
    public int HideTime { get; set; } = 60;
}
public sealed class WarConfig
{
    public bool Enabled { get; set; } = true;
    public int PrepareTime { get; set; } = 30;
}
public sealed class NoScopeConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 15;
}
public sealed class ScoutConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 15;
    public float Gravity { get; set; } = 0.4f;
}
public sealed class TaserConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 15;
}
