namespace Jailbreak;

public sealed class UtilsConfig
{
    public int OpenCellsAfterSeconds { get; set; } = 20;
    public bool HideTeammatesName { get; set; } = true;
    public string BoxStartSound { get; set; } = "tr.BellNormal";
    public float BoxStartSoundVolume { get; set; } = 0.7f;
    public int PrisonerPerGuardRatio { get; set; } = 2; // 2 prisoners per guard. 2 prisoners = 1 guard.
}