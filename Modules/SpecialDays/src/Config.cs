namespace SpecialDays;

public sealed class SDConfig
{
    public KnifeFightConfig KnifeFight { get; set; } = new();
    public FreeForAllConfig FreeForAll { get; set; } = new();
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