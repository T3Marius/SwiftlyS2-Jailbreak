namespace Jailbreak;

public sealed class VoiceConfig
{
    public List<string> MuteOptions { get; set;} = ["both", "prisoners", "guardians"];
    public string MuteWhoWhenWardenSpeaks { get; set; } = "both";

    public float WardenVoiceCheckIntervalSeconds { get; set; } = 0.5f;

    public bool KeepPrisonersMutedDuringRound { get; set; } = true;
    public bool UnmutePrisonersOnRoundEnd { get; set; } = true;
    public int KeepPrisonersMutedForSecondsOnRoundStart { get; set; } = 10;

    public List<string> SkipVoicePenalties { get; set; } = ["jb.admin", "jb.vip"];
}