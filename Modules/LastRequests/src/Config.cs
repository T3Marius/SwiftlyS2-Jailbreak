namespace LastRequests;

public sealed class LRConfig
{
    public KnifeFightConfig  KnifeFight  { get; set; } = new();
    public ShotForShotConfig ShotForShot { get; set; } = new();
    public MagForMagConfig   MagForMag   { get; set; } = new();
    public NoScopeConfig     NoScope     { get; set; } = new();
    public DodgeballConfig   DodgeBall   { get; set; } = new();
}
public sealed class KnifeFightConfig
{
    public bool Enabled { get; set; } = true;
    public float SpeedTypeValue { get; set; } = 2.5f;
    public float GravityTypeValue { get; set; } = 0.5f;
    public int Countdown { get; set; } = 10;

}
public sealed class ShotForShotConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 10;
}
public sealed class MagForMagConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 10;
}
public sealed class NoScopeConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 10;
}
public sealed class DodgeballConfig
{
    public bool Enabled { get; set; } = true;
    public int StartCountdown { get; set; } = 10;
}