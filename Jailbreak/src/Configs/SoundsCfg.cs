namespace Jailbreak;

public sealed class SoundsConfig
{
    public List<string> SoundEventFiles { get; set; } = ["soundevents/soundevents_addon.vsndevts"];
    public bool Enable { get; set; } = true;
    public List<JailbreakSoundReason> MutedReasons { get; set; } =
    [
        JailbreakSoundReason.RoundStart,
        JailbreakSoundReason.RoundEnd,
        JailbreakSoundReason.PluginUnload,
        JailbreakSoundReason.HotReload,
        JailbreakSoundReason.SilentCleanup
    ];

    public JailbreakSoundEntry WardenSet { get; set; } = new() { Name = "jb.WardenSet" };
    public JailbreakSoundEntry YouWarden { get; set; } = new() { Name = "jb.YouWarden" };
    public JailbreakSoundEntry WardenRemoved { get; set; } = new() { Name = "jb.WardenRemoved" };
    public JailbreakSoundEntry RebelSet { get; set; } = new() { Name = "tr.BellNormal" };
    public JailbreakSoundEntry LastRequestAvailable { get; set; } = new() { Name = "jb.LrActivated" };
    public JailbreakSoundEntry LastRequestStarted { get; set; } = new() { Name = "jb.Fight" };
    public JailbreakSoundEntry LastRequestFightBeacon { get; set; } = new() { Name = "jb.FightBeacon" };
    public JailbreakSoundEntry CuffSet { get; set; } = new() { Name = "jb.Cuffs" };
}

public sealed class JailbreakSoundEntry
{
    public bool Enable { get; set; } = true;
    public string Name { get; set; } = "";
    public float Volume { get; set; } = 0.7f;
    public List<JailbreakSoundReason> MutedReasons { get; set; } = [];
}

public enum JailbreakSound
{
    WardenSet,
    YouWarden,
    WardenRemoved,
    RebelSet,
    LastRequestAvailable,
    LastRequestStarted,
    LastRequestFightBeacon,
    CuffSet
}

public enum JailbreakSoundReason
{
    Normal,
    GiveUp,
    Killed,
    RoundStart,
    RoundEnd,
    PluginUnload,
    HotReload,
    SilentCleanup
}
