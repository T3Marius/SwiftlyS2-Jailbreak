using Jailbreak.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;

namespace Jailbreak;

public sealed class LaserManager
{
    private const float MaxDistance = 5000f;
    private const float WeaponForwardOffset = 22f;
    private const float WeaponRightOffset = 10f;
    private const float WeaponDownOffset = 12f;
    private const float LaserWidth = 2.5f;
    private const float LaserGrowSeconds = 0.18f;
    private const float LaserEndLerp = 0.42f;

    private static readonly Color DefaultLaserColor = new(255, 40, 40, 230);

    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly WardenDatabase _wardenDatabase;
    private readonly SpecialDayManager _specialDayManager;
    private readonly TraceParams _laserTraceParams;
    private readonly Dictionary<ulong, LaserState> _lasers = [];
    private readonly HashSet<ulong> _holdingUse = [];

    public LaserManager(ISwiftlyCore core, IJBPlayerManagement players, WardenDatabase wardenDatabase, SpecialDayManager specialDayManager)
    {
        _core = core;
        _players = players;
        _wardenDatabase = wardenDatabase;
        _specialDayManager = specialDayManager;
        _laserTraceParams = CreateLaserTraceParams();
    }

    public void Register()
    {
        _core.Event.OnClientKeyStateChanged += OnClientKeyStateChanged;
        _core.Event.OnTick += OnTick;
        _core.Event.OnMapUnload += OnMapUnload;
    }

    public void Unregister()
    {
        _core.Event.OnClientKeyStateChanged -= OnClientKeyStateChanged;
        _core.Event.OnTick -= OnTick;
        _core.Event.OnMapUnload -= OnMapUnload;
        CleanupAll();
    }

    public void CleanupPlayer(ulong steamId)
    {
        _holdingUse.Remove(steamId);
        RemoveLaser(steamId);
    }

    public void CleanupAll()
    {
        foreach (var steamId in _lasers.Keys.ToList())
            RemoveLaser(steamId);

        _holdingUse.Clear();
    }

    private void OnClientKeyStateChanged(IOnClientKeyStateChangedEvent e)
    {
        if (e.Key != KeyKind.E)
            return;

        var rawPlayer = _core.PlayerManager.GetPlayer(e.PlayerId);
        if (rawPlayer == null)
            return;

        var player = _players.SyncPlayer(rawPlayer);
        if (player == null)
            return;

        if (!e.Pressed)
        {
            _holdingUse.Remove(player.SteamID);
            RemoveLaser(player.SteamID);
            return;
        }

        if (player.IsWarden)
            _holdingUse.Add(player.SteamID);
    }

    private void OnTick()
    {
        foreach (var steamId in _holdingUse.ToList())
        {
            var warden = _players.GetAllPlayers().FirstOrDefault(p => p.SteamID == steamId);
            if (!CanUseLaser(warden))
            {
                CleanupPlayer(steamId);
                continue;
            }

            if ((warden!.Player.PressedButtons & GameButtonFlags.E) == 0)
            {
                CleanupPlayer(steamId);
                continue;
            }

            UpdateLaser(warden);
        }
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        CleanupAll();
    }

    private bool CanUseLaser(IJBPlayer? player)
    {
        return player != null
            && !_specialDayManager.IsSpecialDayActive
            && player.IsWarden
            && player.Player.IsValid
            && player.Player.IsAlive
            && player.Player.PlayerPawn?.IsValid == true;
    }

    private void UpdateLaser(IJBPlayer warden)
    {
        var pawn = warden.Player.PlayerPawn;
        if (pawn == null)
            return;

        pawn.EyeAngles.ToDirectionVectors(out var forward, out var right, out var up);

        var start = GetWeaponPosition(pawn, forward, right, up);
        var state = GetOrCreateLaser(warden.SteamID);
        var beam = state.Beam;
        if (beam == null)
            return;

        var targetEnd = TraceLaserEnd(start, forward);
        var elapsed = (float)(DateTime.UtcNow - state.StartedAt).TotalSeconds;
        var grow = EaseOut(Math.Clamp(elapsed / LaserGrowSeconds, 0f, 1f));
        var targetVisibleEnd = start + ((targetEnd - start) * grow);
        var end = state.LastEnd == null
            ? targetVisibleEnd
            : Lerp(state.LastEnd.Value, targetVisibleEnd, LaserEndLerp);
        var pulse = (MathF.Sin(elapsed * 22f) + 1f) * 0.5f;
        var width = LaserWidth + (pulse * 0.85f);
        var settings = _wardenDatabase.GetWardenSettings(warden.SteamID);
        var baseColor = settings.LaserRainbow
            ? ColorFromHue(elapsed * 0.7f, settings.LaserColor.A)
            : settings.LaserColor;
        var alpha = (byte)(baseColor.A * (0.78f + pulse * 0.22f));

        beam.Teleport(start, null, null);
        beam.EndPos = end;
        beam.Width = width;
        beam.EndWidth = width;
        beam.Render = new Color(baseColor.R, baseColor.G, baseColor.B, alpha);

        beam.EndPosUpdated();
        beam.WidthUpdated();
        beam.EndWidthUpdated();
        beam.RenderUpdated();

        state.LastEnd = end;
    }

    private Vector GetWeaponPosition(CCSPlayerPawn pawn, Vector forward, Vector right, Vector up)
    {
        var eye = pawn.EyePosition ?? pawn.AbsOrigin ?? Vector.Zero;

        return eye
            + (forward * WeaponForwardOffset)
            + (right * WeaponRightOffset)
            - (up * WeaponDownOffset);
    }

    private Vector TraceLaserEnd(Vector start, Vector forward)
    {
        var end = start + (forward * MaxDistance);

        var trace = _core.Trace.TraceShapeLine(start, end, _laserTraceParams);
        return trace.DidHit ? trace.HitPoint : end;
    }

    private static TraceParams CreateLaserTraceParams()
    {
        return TraceParams.Builder()
            .WithLineRay()
            .WithObjectQuery(RnQueryObjectSet.AllGameEntities)
            .WithInteraction(MaskTrace.Solid | MaskTrace.Player | MaskTrace.Hitbox, MaskTrace.Empty, MaskTrace.Solid)
            .WithCollisionGroup(CollisionGroup.PlayerMovement)
            .WithIterateEntities(true)
            .HitSolid()
            .WithShouldHitEntity(entity => entity is not CBeam)
            .Build();
    }

    private LaserState GetOrCreateLaser(ulong steamId)
    {
        if (_lasers.TryGetValue(steamId, out var existing) && existing.Beam?.IsValid == true)
            return existing;

        var beam = _core.EntitySystem.CreateEntity<CBeam>();
        beam.DispatchSpawn();
        ConfigureBeam(beam);
        var state = new LaserState(_core.EntitySystem.GetRefEHandle(beam));
        _lasers[steamId] = state;
        return state;
    }

    private CBeam? GetLaserEntity(ulong steamId)
    {
        if (!_lasers.TryGetValue(steamId, out var state))
            return null;

        var laser = state.Beam;
        return laser?.IsValid == true ? laser : null;
    }

    private void RemoveLaser(ulong steamId)
    {
        if (!_lasers.TryGetValue(steamId, out var state))
            return;

        var laser = state.Beam;
        if (laser?.IsValid == true)
            laser.Despawn();

        _lasers.Remove(steamId);
    }

    private static void ConfigureBeam(CBeam beam)
    {
        beam.BeamType = BeamType_t.BEAM_POINTS;
        beam.NumBeamEnts = 2;
        beam.Width = LaserWidth;
        beam.EndWidth = LaserWidth;
        beam.FadeLength = 0f;
        beam.HaloScale = 0f;
        beam.Amplitude = 0f;
        beam.Speed = 0f;
        beam.FrameRate = 0f;
        beam.ClipStyle = BeamClipStyle_t.kNOCLIP;
        beam.TurnedOff = false;
        beam.RenderMode = RenderMode_t.kRenderTransAlpha;
        beam.RenderFX = RenderFx_t.kRenderFxNone;
        beam.Render = DefaultLaserColor;

        beam.BeamTypeUpdated();
        beam.NumBeamEntsUpdated();
        beam.WidthUpdated();
        beam.EndWidthUpdated();
        beam.FadeLengthUpdated();
        beam.HaloScaleUpdated();
        beam.AmplitudeUpdated();
        beam.SpeedUpdated();
        beam.FrameRateUpdated();
        beam.ClipStyleUpdated();
        beam.TurnedOffUpdated();
        beam.RenderModeUpdated();
        beam.RenderFXUpdated();
        beam.RenderUpdated();
    }

    private static Vector Lerp(Vector from, Vector to, float amount)
    {
        return from + ((to - from) * amount);
    }

    private static float EaseOut(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return 1f - ((1f - value) * (1f - value));
    }

    private static Color ColorFromHue(float hue, byte alpha)
    {
        hue -= MathF.Floor(hue);
        var sector = hue * 6f;
        var x = (byte)(255f * (1f - MathF.Abs((sector % 2f) - 1f)));

        return sector switch
        {
            < 1f => new Color(255, (int)x, 0, alpha),
            < 2f => new Color((int)x, 255, 0, alpha),
            < 3f => new Color(0, 255, (int)x, alpha),
            < 4f => new Color(0, (int)x, 255, alpha),
            < 5f => new Color((int)x, 0, 255, alpha),
            _ => new Color(255, 0, (int)x, alpha)
        };
    }

    private sealed class LaserState(CHandle<CBeam> beamHandle)
    {
        private readonly CHandle<CBeam> _beamHandle = beamHandle;

        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public Vector? LastEnd { get; set; }
        public CBeam? Beam => _beamHandle.Value;
    }
}
