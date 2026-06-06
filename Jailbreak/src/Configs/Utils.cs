namespace Jailbreak;

public sealed class UtilsConfig
{
    public string DatabaseConnection { get; set; } = "default";
    public int OpenCellsAfterSeconds { get; set; } = 20;
    public bool HideTeammatesName { get; set; } = true;
    public string BoxStartSound { get; set; } = "tr.BellNormal";
    public float BoxStartSoundVolume { get; set; } = 0.7f;
    public int PrisonerPerGuardRatio { get; set; } = 2; // 2 prisoners per guard. 2 prisoners = 1 guard.
    public bool AnnounceGuardsDeath { get; set; } = true;
    public BunnyhoopConfig Bunnyhoop { get; set; } = new();
}
public sealed class BunnyhoopConfig
{
    public bool Enable { get; set; } = true;
    public int RoundStartCountdown { get; set; } = 20;
}