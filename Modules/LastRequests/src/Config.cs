namespace LastRequests;

public sealed class LRConfig
{
    public KnifeFightConfig KnifeFight { get; set; } = new();
}
public sealed class KnifeFightConfig
{
    public bool Enabled { get; set; } = true;
    public float SpeedTypeValue { get; set; } = 2.5f;
    public float GravityTypeValue { get; set; } = 0.5f;
    public int Countdown { get; set; } = 10;

}