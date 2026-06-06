using Jailbreak.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class BeaconManager
{
    private const int PingSegments = 96;
    private const int PlayerSegments = 64;
    private const float PingRadius = 100f;
    private const float PingAnimationSeconds = 1.35f;
    private const float PingLifetimeSeconds = 60f;
    private const float PingHeight = 8f;
    private const float PlayerBaseRadius = 48f;
    private const float PlayerRadiusPulse = 14f;
    private const float PlayerHeight = 10f;
    private const float SegmentOverlap = 0.22f;
    private const float UpdateSeconds = 0.05f;

    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly WardenDatabase _wardenDatabase;
    private readonly List<BeaconEffect> _effects = [];
    private readonly Dictionary<ulong, BeaconEffect> _playerEffects = [];

    private Guid? _playerPingHookId;
    private CancellationTokenSource? _updateCts;
    private BeaconEffect? _pingEffect;

    public BeaconManager(ISwiftlyCore core, IJBPlayerManagement players, WardenDatabase wardenDatabase)
    {
        _core = core;
        _players = players;
        _wardenDatabase = wardenDatabase;
    }

    public void Register()
    {
        _playerPingHookId = _core.GameEvent.HookPost<EventPlayerPing>(OnPlayerPing);
        _core.Event.OnMapUnload += OnMapUnload;
    }

    public void Unregister()
    {
        _core.Event.OnMapUnload -= OnMapUnload;

        if (_playerPingHookId.HasValue)
        {
            _core.GameEvent.Unhook(_playerPingHookId.Value);
            _playerPingHookId = null;
        }

        CleanupAll();
    }

    public void CreatePingBeacon(Vector origin)
    {
        CreatePingBeacon(origin, new Color(80, 170, 255, 220), rainbow: false);
    }

    public void CreatePingBeacon(Vector origin, Color color, bool rainbow)
    {
        StopPingBeacon();

        var effect = BeaconEffect.ForLocation(
            new Vector(origin.X, origin.Y, origin.Z + PingHeight),
            PingSegments,
            PingRadius,
            PingAnimationSeconds,
            PingLifetimeSeconds,
            color,
            rainbow);

        SpawnEffect(effect);
        _pingEffect = effect;
    }

    public void StartPlayerBeacon(IPlayer player, float durationSeconds = 0f)
    {
        StartPlayerBeacon(player, new Color(255, 215, 70, 230), durationSeconds);
    }

    public void StartPlayerBeacon(IPlayer player, Color color, float durationSeconds = 0f)
    {
        StartPlayerBeacon(player, color, rainbow: false, durationSeconds);
    }

    public void StartPlayerBeacon(IPlayer player, Color color, bool rainbow, float durationSeconds = 0f)
    {
        if (!player.IsValid || !player.IsAlive || player.PlayerPawn == null)
            return;

        StopPlayerBeacon(player.SteamID);

        var effect = BeaconEffect.ForPlayer(player, PlayerSegments, PlayerBaseRadius, durationSeconds, color, rainbow);
        SpawnEffect(effect);
        _playerEffects[player.SteamID] = effect;
    }

    public void StopPlayerBeacon(ulong steamId)
    {
        if (!_playerEffects.TryGetValue(steamId, out var effect))
            return;

        _playerEffects.Remove(steamId);
        RemoveEffect(effect);
    }

    public void StopPingBeacon()
    {
        if (_pingEffect == null)
            return;

        RemoveEffect(_pingEffect);
        _pingEffect = null;
    }

    public void CleanupAll()
    {
        StopUpdateTimer();

        foreach (var effect in _effects.ToArray())
            effect.Despawn();

        _effects.Clear();
        _playerEffects.Clear();
        _pingEffect = null;
    }

    private HookResult OnPlayerPing(EventPlayerPing e)
    {
        var rawPlayer = e.UserIdPlayer;
        if (rawPlayer == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(rawPlayer);
        if (player == null || !player.IsWarden)
            return HookResult.Continue;

        var settings = _wardenDatabase.GetWardenSettings(player.SteamID);
        CreatePingBeacon(new Vector(e.X, e.Y, e.Z), settings.BeamColor, settings.BeamRainbow);
        return HookResult.Continue;
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        CleanupAll();
    }

    private void SpawnEffect(BeaconEffect effect)
    {
        for (var i = 0; i < effect.Segments; i++)
        {
            var beam = _core.EntitySystem.CreateEntity<CBeam>();
            beam.DispatchSpawn();
            ConfigureBeam(beam, effect.Color);
            effect.BeamHandles.Add(_core.EntitySystem.GetRefEHandle(beam));
        }

        _effects.Add(effect);
        EnsureUpdateTimer();
    }

    private static void ConfigureBeam(CBeam beam, Color color)
    {
        beam.BeamType = BeamType_t.BEAM_POINTS;
        beam.BeamTypeUpdated();

        beam.NumBeamEnts = 2;
        beam.NumBeamEntsUpdated();

        beam.Width = 5f;
        beam.EndWidth = 5f;
        beam.WidthUpdated();
        beam.EndWidthUpdated();

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

    private void EnsureUpdateTimer()
    {
        if (_updateCts != null)
            return;

        _updateCts = _core.Scheduler.RepeatBySeconds(UpdateSeconds, UpdateEffects);
    }

    private void StopUpdateTimer()
    {
        _updateCts?.Cancel();
        _updateCts = null;
    }

    private void UpdateEffects()
    {
        var now = DateTime.UtcNow;

        foreach (var effect in _effects.ToArray())
        {
            if (!effect.Update(now))
                RemoveEffect(effect);
        }

        if (_effects.Count == 0)
            StopUpdateTimer();
    }

    private void RemoveEffect(BeaconEffect effect)
    {
        effect.Despawn();
        _effects.Remove(effect);

        if (effect.PlayerSteamId.HasValue)
            _playerEffects.Remove(effect.PlayerSteamId.Value);

        if (ReferenceEquals(_pingEffect, effect))
            _pingEffect = null;
    }

    private sealed class BeaconEffect
    {
        private readonly Vector _origin;
        private readonly IPlayer? _player;
        private readonly DateTime _startedAt;
        private readonly float _radius;
        private readonly float _animationSeconds;
        private readonly float _lifetimeSeconds;

        public int Segments { get; }
        public Color Color { get; }
        public bool Rainbow { get; }
        public ulong? PlayerSteamId { get; }
        public List<CHandle<CBeam>> BeamHandles { get; } = [];

        private BeaconEffect(Vector origin, IPlayer? player, int segments, float radius, float animationSeconds, float lifetimeSeconds, Color color, bool rainbow)
        {
            _origin = origin;
            _player = player;
            _startedAt = DateTime.UtcNow;
            _radius = radius;
            _animationSeconds = animationSeconds;
            _lifetimeSeconds = lifetimeSeconds;
            Segments = segments;
            Color = color;
            Rainbow = rainbow;
            PlayerSteamId = player?.SteamID;
        }

        public static BeaconEffect ForLocation(Vector origin, int segments, float radius, float animationSeconds, float lifetimeSeconds, Color color, bool rainbow)
        {
            return new BeaconEffect(origin, null, segments, radius, animationSeconds, lifetimeSeconds, color, rainbow);
        }

        public static BeaconEffect ForPlayer(IPlayer player, int segments, float radius, float durationSeconds, Color color, bool rainbow)
        {
            return new BeaconEffect(Vector.Zero, player, segments, radius, 0f, durationSeconds, color, rainbow);
        }

        public bool Update(DateTime now)
        {
            var elapsed = (float)(now - _startedAt).TotalSeconds;
            if (_lifetimeSeconds > 0f && elapsed >= _lifetimeSeconds)
                return false;

            var center = GetCenter();
            if (center == null)
                return false;

            var phase = _animationSeconds > 0f
                ? Math.Clamp(elapsed / _animationSeconds, 0f, 1f)
                : elapsed;

            var radius = _player == null
                ? _radius * EaseOut(phase)
                : _radius + (MathF.Sin(elapsed * 5.5f) * PlayerRadiusPulse);

            var alpha = _player == null
                ? Color.A
                : (byte)(Color.A * (0.72f + (MathF.Sin(elapsed * 5.5f) + 1f) * 0.14f));

            var width = _player == null
                ? 3.5f
                : 4.5f + ((MathF.Sin(elapsed * 5.5f) + 1f) * 1.2f);

            var color = Rainbow ? ColorFromHue(elapsed * 0.45f, alpha) : new Color(Color.R, Color.G, Color.B, alpha);
            UpdateRing(center.Value, Math.Max(1f, radius), Math.Max(1f, width), color);
            return true;
        }

        public void Despawn()
        {
            foreach (var handle in BeamHandles)
            {
                var beam = handle.Value;
                if (beam?.IsValid == true)
                    beam.Despawn();
            }

            BeamHandles.Clear();
        }

        private Vector? GetCenter()
        {
            if (_player == null)
                return _origin;

            if (!_player.IsValid || !_player.IsAlive || _player.PlayerPawn == null)
                return null;

            var origin = _player.PlayerPawn.AbsOrigin;
            if (origin == null)
                return null;

            return new Vector(origin.Value.X, origin.Value.Y, origin.Value.Z + PlayerHeight);
        }

        private void UpdateRing(Vector center, float radius, float width, Color color)
        {
            for (var i = 0; i < BeamHandles.Count; i++)
            {
                var beam = BeamHandles[i].Value;
                if (beam?.IsValid != true)
                    continue;

                var start = GetCirclePoint(center, radius, i - SegmentOverlap);
                var end = GetCirclePoint(center, radius, i + 1f + SegmentOverlap);

                beam.Teleport(start, null, null);
                beam.EndPos = end;
                beam.Width = width;
                beam.EndWidth = width;
                beam.Render = color;

                beam.EndPosUpdated();
                beam.WidthUpdated();
                beam.EndWidthUpdated();
                beam.RenderUpdated();
            }
        }

        private Vector GetCirclePoint(Vector center, float radius, float index)
        {
            var angle = (MathF.Tau / Segments) * index;
            return new Vector(
                center.X + (MathF.Cos(angle) * radius),
                center.Y + (MathF.Sin(angle) * radius),
                center.Z);
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
    }
}
