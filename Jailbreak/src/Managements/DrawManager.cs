using Jailbreak.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;

namespace Jailbreak;

public sealed class DrawManager
{
    private const float MaxDistance = 5000f;
    private const float TraceStartForwardOffset = 8f;
    private const float SelfHitRetryOffset = 48f;
    private const float MinSegmentDistance = 4.5f;
    private const float MaxSegmentDistance = 110f;
    private const float SurfaceOffset = 1.5f;
    private const float BeamWidth = 3.4f;
    private const float JointCapRadius = 2.8f;

    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly SpecialDayManager _specialDayManager;
    private readonly WardenDatabase _wardenDatabase;
    private readonly TraceParams _drawTraceParams;
    private readonly HashSet<ulong> _drawAccess = [];
    private readonly HashSet<ulong> _enabled = [];
    private readonly HashSet<ulong> _holdingMouse2 = [];
    private readonly Dictionary<ulong, DrawState> _states = [];
    private Guid? _roundEndHookId;

    public DrawManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        SpecialDayManager specialDayManager,
        WardenDatabase wardenDatabase)
    {
        _core = core;
        _players = players;
        _specialDayManager = specialDayManager;
        _wardenDatabase = wardenDatabase;
        _drawTraceParams = CreateDrawTraceParams();
    }

    public void Register()
    {
        _core.Event.OnClientKeyStateChanged += OnClientKeyStateChanged;
        _core.Event.OnTick += OnTick;
        _core.Event.OnMapUnload += OnMapUnload;
        _roundEndHookId = _core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
    }

    public void Unregister()
    {
        _core.Event.OnClientKeyStateChanged -= OnClientKeyStateChanged;
        _core.Event.OnTick -= OnTick;
        _core.Event.OnMapUnload -= OnMapUnload;
        Unhook(ref _roundEndHookId);
        CleanupAll();
    }

    public bool ToggleDrawing(IJBPlayer player)
    {
        if (!CanUseDrawing(player))
            return false;

        var playerKey = GetPlayerKey(player);
        if (_enabled.Remove(playerKey))
        {
            _holdingMouse2.Remove(playerKey);
            ResetStroke(playerKey);
            return false;
        }

        _enabled.Add(playerKey);
        return true;
    }

    public bool CanUseDrawing(IJBPlayer player)
    {
        return player.IsWarden
            || (HasDrawAccess(player)
                && player.Team == JBTeam.Prisoner
                && !player.IsRebel
                && !player.IsFreeday);
    }

    public bool HasDrawAccess(IJBPlayer player)
    {
        return _drawAccess.Contains(GetPlayerKey(player));
    }

    public void GrantDrawAccess(IJBPlayer player)
    {
        _drawAccess.Add(GetPlayerKey(player));
    }

    public void RevokeDrawAccess(IJBPlayer player)
    {
        var playerKey = GetPlayerKey(player);
        _drawAccess.Remove(playerKey);
        _enabled.Remove(playerKey);
        _holdingMouse2.Remove(playerKey);
        ResetStroke(playerKey);
    }

    public void ClearRoundAccess()
    {
        foreach (var playerKey in _drawAccess.ToList())
        {
            _enabled.Remove(playerKey);
            _holdingMouse2.Remove(playerKey);
            ResetStroke(playerKey);
        }

        _drawAccess.Clear();
    }

    public bool IsDrawingEnabled(IJBPlayer player)
    {
        return _enabled.Contains(GetPlayerKey(player));
    }

    public void CleanupPlayer(IPlayer player)
    {
        var playerKey = PlayerIdentity.GetKey(player);
        _enabled.Remove(playerKey);
        _drawAccess.Remove(playerKey);
        _holdingMouse2.Remove(playerKey);
        RemoveSegments(playerKey);
        _states.Remove(playerKey);
    }

    public void CleanupAll()
    {
        foreach (var playerKey in _states.Keys.ToList())
            RemoveSegments(playerKey);

        _enabled.Clear();
        _drawAccess.Clear();
        _holdingMouse2.Clear();
        _states.Clear();
    }

    private void OnClientKeyStateChanged(IOnClientKeyStateChangedEvent e)
    {
        if (e.Key != KeyKind.Mouse2)
            return;

        var rawPlayer = _core.PlayerManager.GetPlayer(e.PlayerId);
        if (rawPlayer == null)
            return;

        var player = _players.SyncPlayer(rawPlayer);
        if (player == null)
            return;

        var playerKey = GetPlayerKey(player);
        if (!_enabled.Contains(playerKey))
            return;

        if (!e.Pressed)
        {
            _holdingMouse2.Remove(playerKey);
            ResetStroke(playerKey);
            return;
        }

        _holdingMouse2.Add(playerKey);
    }

    private void OnTick()
    {
        foreach (var playerKey in _holdingMouse2.ToList())
        {
            var player = FindPlayerByKey(playerKey);
            if (!CanDraw(player))
            {
                _holdingMouse2.Remove(playerKey);
                ResetStroke(playerKey);
                continue;
            }

            if ((player!.Player.PressedButtons & GameButtonFlags.Mouse2) == 0)
            {
                _holdingMouse2.Remove(playerKey);
                ResetStroke(playerKey);
                continue;
            }

            UpdateDrawing(player, playerKey);
        }
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        CleanupAll();
    }

    private HookResult OnRoundEnd(EventRoundEnd e)
    {
        CleanupAll();
        return HookResult.Continue;
    }

    private bool CanDraw(IJBPlayer? player)
    {
        return player != null
            && _enabled.Contains(GetPlayerKey(player))
            && !_specialDayManager.IsSpecialDayActive
            && CanUseDrawing(player)
            && player.Player.IsValid
            && player.Player.IsAlive
            && player.Player.PlayerPawn?.IsValid == true;
    }

    private void UpdateDrawing(IJBPlayer player, ulong playerKey)
    {
        var pawn = player.Player.PlayerPawn;
        if (pawn == null)
            return;

        var state = GetState(playerKey);
        pawn.EyeAngles.ToDirectionVectors(out var forward, out _, out _);
        var start = GetEyePosition(pawn) + (forward * TraceStartForwardOffset);
        var trace = TraceDrawSurface(pawn, start, forward);

        if (!trace.DidHit)
        {
            ResetStroke(playerKey);
            return;
        }

        var hitPoint = trace.HitPoint + (trace.HitNormal * SurfaceOffset);

        if (state.LastPoint == null)
        {
            state.LastPoint = hitPoint;
            state.LastNormal = trace.HitNormal;
            return;
        }

        var distance = Distance(state.LastPoint.Value, hitPoint);
        if (distance < MinSegmentDistance)
            return;

        if (distance > MaxSegmentDistance)
        {
            state.LastPoint = hitPoint;
            state.LastNormal = trace.HitNormal;
            return;
        }

        CreateStroke(player, playerKey, state.LastPoint.Value, hitPoint, trace.HitNormal);
        state.LastPoint = hitPoint;
        state.LastNormal = trace.HitNormal;
    }

    private void CreateStroke(IJBPlayer player, ulong playerKey, Vector start, Vector end, Vector normal)
    {
        var color = GetDrawColor(player);
        CreateBeam(playerKey, start, end, color, BeamWidth);
        CreateJointCap(playerKey, end, normal, color);
    }

    private void CreateJointCap(ulong playerKey, Vector point, Vector normal, Color color)
    {
        var (axisA, axisB) = GetSurfaceAxes(normal);
        CreateBeam(playerKey, point - (axisA * JointCapRadius), point + (axisA * JointCapRadius), color, BeamWidth);
        CreateBeam(playerKey, point - (axisB * JointCapRadius), point + (axisB * JointCapRadius), color, BeamWidth);
    }

    private void CreateBeam(ulong playerKey, Vector start, Vector end, Color color, float width)
    {
        var state = GetState(playerKey);

        var beam = _core.EntitySystem.CreateEntity<CBeam>();
        beam.DispatchSpawn();
        ConfigureBeam(beam, color, width);
        beam.Teleport(start, null, null);
        beam.EndPos = end;
        beam.EndPosUpdated();

        state.Segments.Enqueue(_core.EntitySystem.GetRefEHandle(beam));
    }

    public void ClearDrawing(IJBPlayer player)
    {
        var playerKey = GetPlayerKey(player);
        RemoveSegments(playerKey);
        ResetStroke(playerKey);
    }

    public int ClearAllDrawings()
    {
        var cleared = 0;
        foreach (var playerKey in _states.Keys.ToList())
        {
            if (RemoveSegments(playerKey))
                cleared++;

            ResetStroke(playerKey);
        }

        return cleared;
    }

    public bool HasDrawing(IJBPlayer player)
    {
        var playerKey = GetPlayerKey(player);
        return _states.TryGetValue(playerKey, out var state) && state.Segments.Count > 0;
    }

    private Color GetDrawColor(IJBPlayer player)
    {
        var settings = _wardenDatabase.GetWardenSettings(player.SteamID);
        return settings.DrawRainbow
            ? ColorFromHue((float)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 0.22), settings.DrawColor.A)
            : settings.DrawColor;
    }

    private static void ConfigureBeam(CBeam beam, Color color, float width)
    {
        beam.BeamType = BeamType_t.BEAM_POINTS;
        beam.NumBeamEnts = 2;
        beam.Width = width;
        beam.EndWidth = width;
        beam.FadeLength = 0f;
        beam.HaloScale = 0f;
        beam.Amplitude = 0f;
        beam.Speed = 0f;
        beam.FrameRate = 0f;
        beam.ClipStyle = BeamClipStyle_t.kNOCLIP;
        beam.TurnedOff = false;
        beam.RenderMode = RenderMode_t.kRenderTransAlpha;
        beam.RenderFX = RenderFx_t.kRenderFxNone;
        beam.Render = color;

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

    private static (Vector AxisA, Vector AxisB) GetSurfaceAxes(Vector normal)
    {
        var up = MathF.Abs(normal.Z) > 0.92f
            ? new Vector(1, 0, 0)
            : new Vector(0, 0, 1);

        var axisA = Normalize(Cross(normal, up));
        var axisB = Normalize(Cross(normal, axisA));
        return (axisA, axisB);
    }

    private static Vector Cross(Vector a, Vector b)
    {
        return new Vector(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));
    }

    private static Vector Normalize(Vector value)
    {
        var length = MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length <= 0.0001f
            ? new Vector(1, 0, 0)
            : value * (1f / length);
    }

    private TraceResult TraceDrawSurface(CCSPlayerPawn pawn, Vector start, Vector forward)
    {
        var trace = TraceLine(start, forward);
        if (!IsSelfHit(trace, pawn))
            return trace;

        var retryStart = start + (forward * SelfHitRetryOffset);
        var retryTrace = TraceLine(retryStart, forward);

        return retryTrace;
    }

    private TraceResult TraceLine(Vector start, Vector forward)
    {
        var end = start + (forward * MaxDistance);
        var trace = _core.Trace.TraceShapeLine(start, end, _drawTraceParams);
        if (trace.DidHit)
            return trace;

        return _core.Trace.TraceShapeAngle(start, DirectionToAngles(forward), MaxDistance, _drawTraceParams);
    }

    private static bool IsSelfHit(TraceResult trace, CCSPlayerPawn pawn)
    {
        return trace.DidHit
            && trace.Entity?.Address == pawn.Address
            && trace.Fraction <= 0.001f;
    }

    private static TraceParams CreateDrawTraceParams()
    {
        return TraceParams.Builder()
            .WithLineRay()
            .WithObjectQuery(RnQueryObjectSet.All)
            .WithInteraction(
                MaskTrace.Solid
                | MaskTrace.WorldGeometry
                | MaskTrace.StaticLevel
                | MaskTrace.PhysicsProp
                | MaskTrace.Hitbox,
                MaskTrace.Empty,
                MaskTrace.Solid | MaskTrace.WorldGeometry | MaskTrace.StaticLevel | MaskTrace.PhysicsProp)
            .WithCollisionGroup(CollisionGroup.PlayerMovement)
            .WithIterateEntities(true)
            .HitSolid()
            .WithShouldHitEntity(entity => entity is not CBeam and not CCSPlayerPawn)
            .Build();
    }

    private static QAngle DirectionToAngles(Vector forward)
    {
        var yaw = MathF.Atan2(forward.Y, forward.X) * (180f / MathF.PI);
        var pitch = -MathF.Asin(forward.Z) * (180f / MathF.PI);
        return new QAngle(pitch, yaw, 0);
    }

    private static Vector GetEyePosition(CCSPlayerPawn pawn)
    {
        return (pawn.AbsOrigin ?? Vector.Zero) + new Vector(0, 0, 64);
    }

    private DrawState GetState(ulong playerKey)
    {
        if (_states.TryGetValue(playerKey, out var state))
            return state;

        state = new DrawState();
        _states[playerKey] = state;
        return state;
    }

    private void ResetStroke(ulong playerKey)
    {
        if (_states.TryGetValue(playerKey, out var state))
            state.LastPoint = null;
    }

    private bool RemoveSegments(ulong playerKey)
    {
        if (!_states.TryGetValue(playerKey, out var state))
            return false;

        var removed = false;

        foreach (var segment in state.Segments)
        {
            var beam = segment.Value;
            if (beam?.IsValid == true)
            {
                beam.Despawn();
                removed = true;
            }
        }

        state.Segments.Clear();
        state.LastPoint = null;
        return removed;
    }

    private IJBPlayer? FindPlayerByKey(ulong playerKey)
    {
        return _players.GetAllPlayers().FirstOrDefault(p => GetPlayerKey(p) == playerKey);
    }

    private static ulong GetPlayerKey(IJBPlayer player)
    {
        return PlayerIdentity.GetKey(player.Player);
    }

    private static float Distance(Vector a, Vector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private void Unhook(ref Guid? hookId)
    {
        if (!hookId.HasValue)
            return;

        _core.GameEvent.Unhook(hookId.Value);
        hookId = null;
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

    private sealed class DrawState
    {
        public Vector? LastPoint { get; set; }
        public Vector? LastNormal { get; set; }
        public Queue<CHandle<CBeam>> Segments { get; } = [];
    }
}
