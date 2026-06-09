using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using Jailbreak.Contract;
using SwiftlyS2.Shared.Sounds;

namespace Jailbreak;

public sealed class JailbreakSoundManager
{
    private readonly ISwiftlyCore _core;
    private readonly SoundsConfig _config;

    public JailbreakSoundManager(ISwiftlyCore core, IOptions<SoundsConfig> config)
    {
        _core = core;
        _config = config.Value;
    }

    public void Play(JailbreakSound sound, JailbreakSoundReason reason = JailbreakSoundReason.Normal)
    {
        var entry = GetPlayableEntry(sound, reason);
        if (entry == null)
            return;

        var name = entry.Name.Trim();
        var volume = Math.Clamp(entry.Volume, 0.0f, 1.0f);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            _core.EmitSoundToAll(name, volume);
        });
    }

    public void PlayToPlayer(IJBPlayer player, JailbreakSound sound, JailbreakSoundReason reason = JailbreakSoundReason.Normal)
    {
        var entry = GetPlayableEntry(sound, reason);
        if (entry == null || !player.Player.IsValid)
            return;

        var name = entry.Name.Trim();
        var volume = Math.Clamp(entry.Volume, 0.0f, 1.0f);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (player.Player.IsValid)
                player.Player.EmitSoundToPlayer(name, volume);
        });
    }

    public void PlayFromPlayer(IJBPlayer source, JailbreakSound sound, JailbreakSoundReason reason = JailbreakSoundReason.Normal)
    {
        var entry = GetPlayableEntry(sound, reason);
        if (entry == null || !source.Player.IsValid)
            return;

        var name = entry.Name.Trim();
        var volume = Math.Clamp(entry.Volume, 0.0f, 1.0f);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (!source.Player.IsValid)
                return;

            var pawn = source.Player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                return;

            var soundEvent = new SoundEvent
            {
                Name = name,
                Volume = volume
            };

            soundEvent.SetSourceEntity(pawn);
            soundEvent.Recipients.AddAllPlayers();
            soundEvent.Emit();
        });
    }

    private JailbreakSoundEntry? GetPlayableEntry(JailbreakSound sound, JailbreakSoundReason reason)
    {
        if (!_config.Enable || IsMuted(_config.MutedReasons, reason))
            return null;

        var entry = GetEntry(sound);
        if (entry == null || !entry.Enable || string.IsNullOrWhiteSpace(entry.Name) || IsMuted(entry.MutedReasons, reason))
            return null;

        return entry;
    }

    private JailbreakSoundEntry? GetEntry(JailbreakSound sound)
    {
        return sound switch
        {
            JailbreakSound.WardenSet => _config.WardenSet,
            JailbreakSound.YouWarden => _config.YouWarden,
            JailbreakSound.WardenRemoved => _config.WardenRemoved,
            JailbreakSound.RebelSet => _config.RebelSet,
            JailbreakSound.LastRequestAvailable => _config.LastRequestAvailable,
            JailbreakSound.LastRequestStarted => _config.LastRequestStarted,
            JailbreakSound.LastRequestFightBeacon => _config.LastRequestFightBeacon,
            JailbreakSound.CuffSet => _config.CuffSet,
            _ => null
        };
    }

    private static bool IsMuted(IEnumerable<JailbreakSoundReason> mutedReasons, JailbreakSoundReason reason)
    {
        return mutedReasons.Contains(reason);
    }
}
