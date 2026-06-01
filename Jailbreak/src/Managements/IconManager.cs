using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

/// <summary>
/// Manages particle-system icons that float above players to indicate their Jailbreak role.
/// Registered as a DI singleton so the active-icon dictionary survives across player additions
/// and is properly cleaned up on plugin unload.
/// </summary>
public sealed class IconManager
{
    private readonly ISwiftlyCore _core;
    private readonly Dictionary<ulong, CParticleSystem> _activeIcons = [];

    public IconManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void CreateIcon(IPlayer player, string particlePath, float heightOffset = 72.0f)
    {
        if (!player.IsValid || string.IsNullOrEmpty(particlePath))
            return;

        var pawn = player.Pawn;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
            return;

        RemoveIcon(player.SteamID);

        var particle = _core.EntitySystem.CreateEntityByDesignerName<CParticleSystem>("info_particle_system");
        if (particle == null)
            return;

        using var kv = new CEntityKeyValues();
        kv.SetString("effect_name", particlePath);
        kv.SetBool("start_active", false);
        kv.SetVector("origin", pawn.AbsOrigin.Value + new Vector(0, 0, heightOffset));
        particle.DispatchSpawn(kv);

        particle.AcceptInput<string>("Start", null);
        particle.AcceptInput("SetParent", "!activator", pawn, pawn);
        particle.Active = true;
        particle.ActiveUpdated();
        particle.SetTransmitState(true);

        _activeIcons[player.SteamID] = particle;
    }

    public void RemoveIcon(ulong steamId)
    {
        if (!_activeIcons.TryGetValue(steamId, out var particle))
            return;

        if (particle != null && particle.IsValid)
        {
            particle.AcceptInput<string>("Stop", null);
            particle.AcceptInput<string>("DestroyImmediately", null);
            particle.Active = false;
            particle.ActiveUpdated();
        }

        _activeIcons.Remove(steamId);
    }

    /// <summary>Removes every active icon — called during plugin unload.</summary>
    public void CleanupAll()
    {
        foreach (var steamId in _activeIcons.Keys.ToList())
            RemoveIcon(steamId);
    }
}
