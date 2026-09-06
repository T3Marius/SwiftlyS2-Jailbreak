using HudText.Contract;
using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class LastRequestManager
{
    public bool WasLastRequestActiveThisFrame { get; private set; }

    private const float DuelBeamWidth = 2.4f;
    private const float DuelBeamHeight = 48f;

    private static readonly Color PrisonerBeaconColor = new(255, 190, 55, 235);
    private static readonly Color GuardBeaconColor = new(80, 170, 255, 235);
    private static readonly Color DuelBeamColor = new(255, 255, 255, 210);

    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly BeaconManager _beaconManager;
    private readonly CuffsManager _cuffsManager;
    private readonly JBStatsDB _statsDB;
    private readonly SpecialDayManager _specialDayManager;
    private readonly JailbreakSoundManager _soundManager;
    private readonly HudConfig _hudConfig;
    private readonly ILogger<LastRequestManager> _log;
    private readonly Dictionary<string, ILastRequest> _lastRequests = new(StringComparer.OrdinalIgnoreCase);

    private Guid? _playerDeathHookId;
    private Guid? _playerDisconnectHookId;
    private Guid? _roundEndHookId;
    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _fightBeaconSoundCts;
    private IHudTextService? _hudText;
    private readonly Dictionary<int, HudTextHandle> _lastRequestInfoHuds = [];
    private readonly Dictionary<int, HudTextHandle> _lastRequestCountdownHuds = [];
    private LastRequestStartContext? _currentContext;
    private CHandle<CBeam>? _duelBeamHandle;
    private bool _currentStarted;
    private bool _countdownActive;
    private int? _countdownSecondsRemaining;
    private bool _availableSoundPlayed;
    private bool _isMapUnloading;

    public LastRequestManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        BeaconManager beaconManager,
        CuffsManager cuffsManager,
        JBStatsDB statsDB,
        SpecialDayManager specialDayManager,
        JailbreakSoundManager soundManager,
        IOptions<HudConfig> hudConfig,
        ILogger<LastRequestManager> log)
    {
        _core = core;
        _players = players;
        _beaconManager = beaconManager;
        _cuffsManager = cuffsManager;
        _statsDB = statsDB;
        _specialDayManager = specialDayManager;
        _soundManager = soundManager;
        _hudConfig = hudConfig.Value;
        _log = log;
    }

    public IReadOnlyCollection<ILastRequest> LastRequests => _lastRequests.Values.ToArray();
    public ILastRequest? CurrentLastRequest { get; private set; }
    public bool IsLastRequestActive => CurrentLastRequest != null;
    public bool IsCountdownActive => _countdownActive;
    public event Action? StateChanged;

    public void Register()
    {
        _playerDeathHookId = _core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
        _playerDisconnectHookId = _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        _roundEndHookId = _core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        _core.GameHooks.Entities.TakeDamage.Pre += OnEntityTakeDamage;
        _core.GameHooks.Items.CanAcquire.Post += OnItemServicesCanAcquire;
        _core.Event.OnMapUnload += OnMapUnload;
        _core.Event.OnClientDisconnected += OnClientDisconnected;
        _core.Event.OnClientPutInServer += OnClientPutInServer;
        _core.Event.OnTick += OnTick;
    }

    public void Unregister()
    {
        Unhook(ref _playerDeathHookId);
        Unhook(ref _playerDisconnectHookId);
        Unhook(ref _roundEndHookId);
        _core.GameHooks.Entities.TakeDamage.Pre -= OnEntityTakeDamage;
        _core.GameHooks.Items.CanAcquire.Post -= OnItemServicesCanAcquire;
        _core.Event.OnMapUnload -= OnMapUnload;
        _core.Event.OnClientDisconnected -= OnClientDisconnected;
        _core.Event.OnClientPutInServer -= OnClientPutInServer;
        _core.Event.OnTick -= OnTick;

        EndLastRequest(null, null, announce: false);
        _lastRequests.Clear();
    }

    public void SetHudTextService(IHudTextService? hudText)
    {
        if (ReferenceEquals(_hudText, hudText))
            return;

        StopLastRequestHuds();
        _hudText = hudText;

        if (_hudText != null && CurrentLastRequest != null && _currentContext != null)
        {
            UpdateInfoHud(CurrentLastRequest, _currentContext);
            if (_countdownSecondsRemaining is int remaining)
                UpdateCountdownHud(CurrentLastRequest, remaining);
        }
    }

    public bool RegisterLastRequest(ILastRequest lastRequest)
    {
        if (string.IsNullOrWhiteSpace(lastRequest.Id))
            throw new ArgumentException("Last Request id cannot be empty.", nameof(lastRequest));

        if (!_lastRequests.TryAdd(lastRequest.Id, lastRequest))
        {
            _log.LogWarning("Last Request registration skipped because id is already registered. Id={Id}", lastRequest.Id);
            return false;
        }

        _log.LogInformation("Registered Last Request. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
        return true;
    }

    public bool UnregisterLastRequest(string id)
    {
        if (!_lastRequests.TryGetValue(id, out var lastRequest))
            return false;

        if (ReferenceEquals(CurrentLastRequest, lastRequest))
            EndLastRequest(null, null);

        _lastRequests.Remove(id);
        _log.LogInformation("Unregistered Last Request. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
        return true;
    }

    public bool TryGetLastRequest(string id, out ILastRequest lastRequest)
    {
        return _lastRequests.TryGetValue(id, out lastRequest!);
    }

    public bool StartLastRequest(ILastRequest lastRequest, LastRequestStartContext context)
    {
        if (CurrentLastRequest != null || !lastRequest.CanStart(context))
            return false;

        CurrentLastRequest = lastRequest;
        StateChanged?.Invoke();
        _currentContext = context;
        _currentStarted = false;
        _isMapUnloading = false;

        StopFightBeaconSound();

        DisableCurrentWarden();
        ApplyVisuals(context);
        AnnounceLastRequestSelected(lastRequest, context);
        UpdateInfoHud(lastRequest, context);

        if (lastRequest.StartCountdown <= 0)
        {
            StartCurrentLastRequest(lastRequest, context);
            return true;
        }

        StartCountdown(lastRequest, context);
        return true;
    }

    public void EndLastRequest(IJBPlayer? winner, IJBPlayer? loser)
    {
        EndLastRequest(winner, loser, announce: true);
    }

    public bool CanUseLastRequest(IJBPlayer player)
    {
        return CurrentLastRequest == null
            && player.Team == JBTeam.Prisoner
            && player.Player.IsValid
            && player.Player.IsAlive
            && !player.IsRebel
            && GetEligibleLastRequestPrisoners().Count == 1;
    }

    public IReadOnlyList<IJBPlayer> GetEligibleLastRequestPrisoners()
    {
        if (_specialDayManager.HasQueuedOrActiveSpecialDay || CurrentLastRequest != null)
            return [];

        return _players.GetPlayersByTeam(JBTeam.Prisoner)
            .Where(IsEligibleLastRequestPrisoner)
            .ToArray();
    }

    private void StartCountdown(ILastRequest lastRequest, LastRequestStartContext context)
    {
        StopCountdown();
        _countdownActive = true;
        var remaining = lastRequest.StartCountdown;
        _countdownSecondsRemaining = remaining;

        UpdateCountdownHud(lastRequest, remaining);
        _countdownCts = _core.Scheduler.RepeatBySeconds(1f, () =>
        {
            if (CurrentLastRequest != lastRequest || _currentContext != context)
            {
                StopCountdown();
                return;
            }

            remaining--;
            _countdownSecondsRemaining = remaining;
            if (remaining <= 0)
            {
                StopCountdown();
                StartCurrentLastRequest(lastRequest, context);
                return;
            }

            UpdateCountdownHud(lastRequest, remaining);
        });
    }

    private void StartCurrentLastRequest(ILastRequest lastRequest, LastRequestStartContext context)
    {
        _countdownActive = false;
        StopCountdownHud();

        try
        {
            ApplyStartLoadout(lastRequest, context);
            // Mark the module active before invoking external code so a partial
            // start is still cleaned up if Start throws after registering hooks.
            _currentStarted = true;
            lastRequest.Start(context);
            ResetParticipantHealth(lastRequest, context);
            _soundManager.Play(JailbreakSound.LastRequestStarted);
            StartFightBeaconSound();
            _players.SendMessage(MessageType.Chat, "last_request_started", true, args: [lastRequest.Name, context.Prisoner.Player.Name]);
            _log.LogInformation("Started Last Request. Id={Id}, Name={Name}, Prisoner={Prisoner}, Guard={Guard}",
                lastRequest.Id,
                lastRequest.Name,
                context.Prisoner.SteamID,
                context.Guard?.SteamID);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start Last Request. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
            EndLastRequest(null, null, announce: false);
        }
    }

    private void EndLastRequest(IJBPlayer? winner, IJBPlayer? loser, bool announce)
    {
        StopCountdown();
        StopFightBeaconSound();
        StopLastRequestHuds();

        var lastRequest = CurrentLastRequest;
        if (lastRequest == null)
            return;

        if (winner != null && winner.Team == JBTeam.Prisoner)
        {
            StripWeapons(winner.Player);
            _core.Scheduler.NextWorldUpdate(() => GiveWeapon(winner.Player, winner.Team == JBTeam.Prisoner ? "weapon_knife_t" : "weapon_knife"));
        }

        CurrentLastRequest = null;
        StateChanged?.Invoke();
        _countdownActive = false;

        if (_currentStarted)
        {
            try
            {
                lastRequest.End(winner, loser);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to clean up Last Request. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
            }
        }

        _currentStarted = false;
        CleanupVisuals();
        _currentContext = null;

        var winnerWins = RecordLastRequestStats(winner, loser);

        if (announce)
            AnnounceLastRequestEnded(lastRequest, winner, winnerWins);

        _log.LogInformation("Ended Last Request. Id={Id}, Name={Name}, Winner={Winner}, Loser={Loser}",
            lastRequest.Id,
            lastRequest.Name,
            winner?.SteamID,
            loser?.SteamID);
    }

    private HookResult OnPlayerDeath(EventPlayerDeath e)
    {
        var lastRequest = CurrentLastRequest;
        if (lastRequest == null || _currentContext == null || e.UserIdPlayer == null)
        {
            QueueAvailabilityCheck();
            return HookResult.Continue;
        }

        var victim = _players.SyncPlayer(e.UserIdPlayer);
        var attacker = e.AttackerPlayer == null ? null : _players.SyncPlayer(e.AttackerPlayer);
        if (victim == null || !ShouldEndOnDeath(_currentContext, victim))
            return HookResult.Continue;

        var winner = GetDeathWinner(_currentContext, victim, attacker);

        if (_currentStarted)
        {
            try
            {
                lastRequest.OnPlayerDied(victim, attacker);
                _currentStarted = false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Last Request death callback failed. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
            }
        }

        EndLastRequest(winner, victim);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e)
    {
        var lastRequest = CurrentLastRequest;
        if (lastRequest == null || e.UserIdPlayer == null)
        {
            QueueAvailabilityCheck();
            return HookResult.Continue;
        }

        var player = _players.SyncPlayer(e.UserIdPlayer);
        if (player == null || _currentContext == null || !ShouldEndOnDisconnect(_currentContext, player))
            return HookResult.Continue;

        var winner = GetDisconnectWinner(_currentContext, player);

        if (_currentStarted)
        {
            try
            {
                lastRequest.OnPlayerDisconnected(player);
                _currentStarted = false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Last Request disconnect callback failed. Id={Id}, Name={Name}", lastRequest.Id, lastRequest.Name);
            }
        }

        EndLastRequest(winner, player);
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd e)
    {
        _availableSoundPlayed = false;
        EndLastRequest(null, null, announce: false);
        return HookResult.Continue;
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        _isMapUnloading = true;
        _availableSoundPlayed = false;
        EndLastRequest(null, null, announce: false);
    }

    private void OnEntityTakeDamage(ref TakeDamageEntityPreContext ctx)
    {
        var e = ctx.Params;
        var lastRequest = CurrentLastRequest;
        if (lastRequest == null || _currentContext == null)
            return;

        var rawVictim = GetPlayerFromEntity(e.Entity);
        var victim = rawVictim == null ? null : _players.SyncPlayer(rawVictim);

        var attackerPawn = e.Info.AttackerInfo.AttackerPawn.Value;
        var rawAttacker = attackerPawn?.ToPlayer();
        var attacker = rawAttacker == null ? null : _players.SyncPlayer(rawAttacker);

        if (!LastRequestRules.CanDamage(lastRequest, _currentContext, victim, attacker, _currentStarted))
        {
            BlockDamage(ref ctx);
            return;
        }

        if (attacker != null && IsParticipant(lastRequest, _currentContext, attacker)
            && !IsAllowedDamageWeapon(lastRequest, _currentContext, attacker, e.Info))
            BlockDamage(ref ctx);
    }

    private void OnItemServicesCanAcquire(ref CanAcquireItemPostContext ctx)
    {
        if (CurrentLastRequest is not { } request || _currentContext is not { } context
            || ctx.Params.Player is not { } rawPlayer)
            return;
        var player = _players.SyncPlayer(rawPlayer);
        if (player != null && IsParticipant(request, context, player)
            && !LastRequestRules.IsWeaponAllowed(request, context, (ItemDefinitionIndex)ctx.Params.EconItemView.ItemDefinitionIndex))
            ctx.Return = AcquireResult.NotAllowedByProhibition;
    }

    private bool IsAllowedDamageWeapon(ILastRequest request, LastRequestStartContext context,
        IJBPlayer attacker, CTakeDamageInfo info)
    {
        // Resolve the damage source before the held weapon: a thrown grenade can
        // hit after its owner has switched weapons.
        var source = info.Ability.Value;
        var inflictor = info.Inflictor.Value;
        // Fire persists after the thrower switches weapons. Never classify it
        // as damage from the gun they happen to be holding now.
        if (inflictor?.IsValid == true && inflictor.DesignerName == "inferno")
        {
            IEnumerable<ItemDefinitionIndex> fireWeapons = context.SelectedWeapon is { } selectedFire
                ? new[] { selectedFire } : request.AllowedWeapons;
            return (context.SelectedWeapon == null && request.AllowAllWeapons)
                || fireWeapons.Any(weapon => _core.Helpers.GetClassnameByDefinitionIndex(weapon)
                    is "weapon_molotov" or "weapon_incgrenade");
        }
        var name = source?.IsValid == true ? source.DesignerName : null;
        if (inflictor?.IsValid == true && inflictor.DesignerName.EndsWith("_projectile", StringComparison.Ordinal))
            name = "weapon_" + inflictor.DesignerName[..^"_projectile".Length];
        if (string.IsNullOrEmpty(name) || !name.StartsWith("weapon_", StringComparison.Ordinal))
            name = attacker.Player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value?.DesignerName;
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Contains("knife", StringComparison.OrdinalIgnoreCase)
            && context.SelectedWeapon == null && request.AllowedWeapons.Overlaps(LastRequestWeapons.AllKnives))
            return true;
        IEnumerable<ItemDefinitionIndex> allowed = context.SelectedWeapon is { } selected ? new[] { selected } : request.AllowedWeapons;
        return (context.SelectedWeapon == null && request.AllowAllWeapons)
            || allowed.Any(weapon => _core.Helpers.GetClassnameByDefinitionIndex(weapon) == name);
    }

    private void EnforceWeapons()
    {
        if (!_currentStarted || CurrentLastRequest is not { } request || _currentContext is not { } context)
            return;
        foreach (var player in GetLoadoutPlayers(request, context))
        {
            var services = player.Player.PlayerPawn?.WeaponServices;
            if (services == null) continue;
            foreach (var weapon in services.MyValidWeapons.ToArray())
                if (weapon.IsValid && !LastRequestRules.IsWeaponAllowed(request, context,
                    (ItemDefinitionIndex)weapon.As<CEconEntity>().AttributeManager.Item.ItemDefinitionIndex))
                    services.RemoveWeapon(weapon);
        }
    }

    private void OnTick()
    {
        WasLastRequestActiveThisFrame = IsLastRequestActive;
        UpdateDuelBeam();
        EnforceWeapons();
    }

    private void DisableCurrentWarden()
    {
        var warden = _players.GetWarden();
        if (warden != null)
        {
            _cuffsManager.OnWardenRemove(warden);
            warden.SetWarden(false, silent: true);
        }

        foreach (var player in _players.GetAllPlayers())
            player.CanBecomeWarden = false;
    }

    private void ApplyVisuals(LastRequestStartContext context)
    {
        _beaconManager.StartPlayerBeacon(context.Prisoner.Player, PrisonerBeaconColor, rainbow: false);

        if (context.Guard != null)
            _beaconManager.StartPlayerBeacon(context.Guard.Player, GuardBeaconColor, rainbow: false);

        if (context.Guard != null)
            EnsureDuelBeam();
    }

    private void CleanupVisuals()
    {
        if (_currentContext != null)
        {
            _beaconManager.StopPlayerBeacon(PlayerIdentity.GetKey(_currentContext.Prisoner.Player));

            if (_currentContext.Guard != null)
                _beaconManager.StopPlayerBeacon(PlayerIdentity.GetKey(_currentContext.Guard.Player));
        }

        RemoveDuelBeam();
    }

    private void EnsureDuelBeam()
    {
        if (_duelBeamHandle?.Value?.IsValid == true)
            return;

        var beam = _core.EntitySystem.CreateEntity<CBeam>();
        beam.DispatchSpawn();
        ConfigureDuelBeam(beam);
        _duelBeamHandle = _core.EntitySystem.GetRefEHandle(beam);
    }

    private void UpdateDuelBeam()
    {
        if (CurrentLastRequest == null || _currentContext?.Guard == null)
            return;

        var beam = _duelBeamHandle?.Value;
        if (beam?.IsValid != true)
        {
            EnsureDuelBeam();
            beam = _duelBeamHandle?.Value;
            if (beam?.IsValid != true)
                return;
        }

        if (!TryGetBeamPoint(_currentContext.Prisoner, out var start) || !TryGetBeamPoint(_currentContext.Guard, out var end))
            return;

        beam.Teleport(start, null, null);
        beam.EndPos = end;
        beam.EndPosUpdated();
    }

    private void RemoveDuelBeam()
    {
        var beam = _duelBeamHandle?.Value;
        if (beam?.IsValid == true)
            beam.Despawn();

        _duelBeamHandle = null;
    }

    private static bool TryGetBeamPoint(IJBPlayer player, out Vector point)
    {
        point = Vector.Zero;

        if (!player.Player.IsValid || player.Player.PlayerPawn == null)
            return false;

        var origin = player.Player.PlayerPawn.AbsOrigin;
        if (origin == null)
            return false;

        point = new Vector(origin.Value.X, origin.Value.Y, origin.Value.Z + DuelBeamHeight);
        return true;
    }

    private static void ConfigureDuelBeam(CBeam beam)
    {
        beam.BeamType = BeamType_t.BEAM_POINTS;
        beam.NumBeamEnts = 2;
        beam.Width = DuelBeamWidth;
        beam.EndWidth = DuelBeamWidth;
        beam.FadeLength = 0f;
        beam.HaloScale = 0f;
        beam.Amplitude = 0f;
        beam.Speed = 0f;
        beam.FrameRate = 0f;
        beam.TurnedOff = false;
        beam.RenderMode = RenderMode_t.kRenderTransAlpha;
        beam.RenderFX = RenderFx_t.kRenderFxNone;
        beam.Render = DuelBeamColor;

        beam.BeamTypeUpdated();
        beam.NumBeamEntsUpdated();
        beam.WidthUpdated();
        beam.EndWidthUpdated();
        beam.FadeLengthUpdated();
        beam.HaloScaleUpdated();
        beam.AmplitudeUpdated();
        beam.SpeedUpdated();
        beam.FrameRateUpdated();
        beam.TurnedOffUpdated();
        beam.RenderModeUpdated();
        beam.RenderFXUpdated();
        beam.RenderUpdated();
    }

    private void UpdateInfoHud(ILastRequest lastRequest, LastRequestStartContext context)
    {
        UpdateHudForPlayers(
            lastRequest,
            _lastRequestInfoHuds,
            _hudConfig.CurrentLastRequestHud,
            player => player.Localizer[
                "last_request_active_hud",
                lastRequest.Name,
                lastRequest.Description,
                FormatVariant(player, context)]);
    }

    private void UpdateCountdownHud(ILastRequest lastRequest, int remaining)
    {
        UpdateHudForPlayers(
            lastRequest,
            _lastRequestCountdownHuds,
            _hudConfig.LastRequestCountdownHud,
            player => player.Localizer["last_request_countdown_hud", lastRequest.Name, remaining]);
    }

    private void UpdateHudForPlayers(
        ILastRequest lastRequest,
        Dictionary<int, HudTextHandle> huds,
        HudTextSettings style,
        Func<IJBPlayer, string> messageFactory)
    {
        if (_isMapUnloading || _hudText is null || !ReferenceEquals(CurrentLastRequest, lastRequest))
            return;

        foreach (var player in _players.GetAllPlayers().Where(player => player.Player.IsValid))
        {
            var playerId = player.Player.PlayerID;
            var message = messageFactory(player);

            if (huds.TryGetValue(playerId, out var hud))
            {
                try
                {
                    _hudText.UpdateHud(hud, message);
                    _hudText.ShowHud(hud);
                    continue;
                }
                catch (ArgumentException)
                {
                    huds.Remove(playerId);
                }
            }

            huds[playerId] = _hudText.CreateHud(playerId, message, ToHudTextOptions(style));
        }
    }

    private static string FormatVariant(IJBPlayer player, LastRequestStartContext context)
    {
        if (context.SelectedVariant == null)
            return player.Localizer["last_request_type_normal"];

        return string.IsNullOrWhiteSpace(context.SelectedVariant.Description)
            ? context.SelectedVariant.Name
            : $"{context.SelectedVariant.Name}: {context.SelectedVariant.Description}";
    }

    private void StopLastRequestHuds()
    {
        if (_isMapUnloading)
        {
            _lastRequestInfoHuds.Clear();
            _lastRequestCountdownHuds.Clear();
            return;
        }

        if (_hudText != null)
        {
            foreach (var hud in _lastRequestInfoHuds.Values)
                RemoveHudSafely(hud);

            foreach (var hud in _lastRequestCountdownHuds.Values)
                RemoveHudSafely(hud);
        }

        _lastRequestInfoHuds.Clear();
        _lastRequestCountdownHuds.Clear();
    }

    private void StopCountdownHud()
    {
        if (!_isMapUnloading && _hudText != null)
        {
            foreach (var hud in _lastRequestCountdownHuds.Values)
                RemoveHudSafely(hud);
        }

        _lastRequestCountdownHuds.Clear();
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        RemovePlayerHud(_lastRequestInfoHuds, @event.PlayerId);
        RemovePlayerHud(_lastRequestCountdownHuds, @event.PlayerId);
    }

    private void OnClientPutInServer(IOnClientPutInServerEvent @event)
    {
        _isMapUnloading = false;
        var lastRequest = CurrentLastRequest;
        var context = _currentContext;
        if (_hudText is null || lastRequest is null || context is null)
            return;

        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (ReferenceEquals(CurrentLastRequest, lastRequest) && ReferenceEquals(_currentContext, context))
            {
                UpdateInfoHud(lastRequest, context);
                if (_countdownSecondsRemaining is int remaining)
                    UpdateCountdownHud(lastRequest, remaining);
            }
        });
    }

    private void RemovePlayerHud(Dictionary<int, HudTextHandle> huds, int playerId)
    {
        if (!_isMapUnloading && _hudText != null && huds.Remove(playerId, out var hud))
            RemoveHudSafely(hud);
        else
            huds.Remove(playerId);
    }

    private void RemoveHudSafely(HudTextHandle hud)
    {
        try
        {
            _hudText!.RemoveHud(hud);
        }
        catch (ArgumentException)
        {
            // HudText may already have discarded the handle during a transition.
        }
    }

    private static HudTextOptions ToHudTextOptions(HudTextSettings style) => new()
    {
        Position = style.Position,
        Color = style.Color,
        Size = style.Size,
        Background = style.Background,
        BackgroundOpacity = style.BackgroundOpacity,
        DropShadow = style.DropShadow,
        OutlineColor = style.OutlineColor,
        Font = style.Font,
        FontWeight = style.FontWeight,
        TextAlignment = style.TextAlignment,
        // Position already supplies the vertical anchor. Passing the shared
        // settings default (Top) here would override CenterMiddle/CenterBottom.
        MarginBottom = style.MarginBottom,
        MarginTop = style.MarginTop,
        MarginLeft = style.MarginLeft,
        MarginRight = style.MarginRight,
    };

    private void StopCountdown()
    {
        _countdownCts?.Cancel();
        _countdownCts = null;
        _countdownSecondsRemaining = null;
        StopCountdownHud();
    }

    private void StartFightBeaconSound()
    {
        StopFightBeaconSound();

        _fightBeaconSoundCts = _core.Scheduler.RepeatBySeconds(1.0f, () =>
        {
            if (!IsLastRequestActive || !_currentStarted)
            {
                StopFightBeaconSound();
                return;
            }

            _soundManager.Play(JailbreakSound.LastRequestFightBeacon);
        });
    }

    private void StopFightBeaconSound()
    {
        _fightBeaconSoundCts?.Cancel();
        _fightBeaconSoundCts = null;
    }

    private void QueueAvailabilityCheck()
    {
        _core.Scheduler.NextWorldUpdate(CheckAndAnnounceAvailability);
    }

    private void CheckAndAnnounceAvailability()
    {
        if (_availableSoundPlayed || CurrentLastRequest != null || _specialDayManager.HasQueuedOrActiveSpecialDay)
            return;

        if (GetEligibleLastRequestPrisoners().Count != 1)
            return;

        _availableSoundPlayed = true;
        _soundManager.Play(JailbreakSound.LastRequestAvailable);
    }

    private void ResetParticipantHealth(ILastRequest lastRequest, LastRequestStartContext context)
    {
        foreach (var player in GetLoadoutPlayers(lastRequest, context))
        {
            if (!IsAlive(player))
                continue;

            var pawn = player.Player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                continue;

            pawn.MaxHealth = 100;
            pawn.Health = 100;
            pawn.MaxHealthUpdated();
            pawn.HealthUpdated();
        }
    }

    private void ApplyStartLoadout(ILastRequest lastRequest, LastRequestStartContext context)
    {
        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (!ReferenceEquals(CurrentLastRequest, lastRequest) || !ReferenceEquals(_currentContext, context))
                return;
            foreach (var player in GetLoadoutPlayers(lastRequest, context))
            {
                if (!player.Player.IsValid || !player.Player.IsAlive)
                    continue;

                if (lastRequest.StripWeaponsOnStart)
                    StripWeapons(player.Player);

                foreach (var weaponName in lastRequest.GiveWeaponsOnStart)
                    GiveWeapon(player.Player, weaponName);

                if (context.SelectedWeapon.HasValue)
                    GiveSelectedWeapon(player.Player, context.SelectedWeapon.Value);
            }
        });
    }

    private IEnumerable<IJBPlayer> GetLoadoutPlayers(ILastRequest lastRequest, LastRequestStartContext context)
    {
        yield return context.Prisoner;

        if (context.Guard != null)
        {
            yield return context.Guard;
            yield break;
        }

        if (lastRequest.OpponentMode == LastRequestOpponentMode.PrisonerVsAllGuards)
        {
            foreach (var guard in _players.GetPlayersByTeam(JBTeam.Guard).Where(IsAlive))
                yield return guard;
        }
    }

    private void GiveSelectedWeapon(IPlayer player, ItemDefinitionIndex weapon)
    {
        var classname = _core.Helpers.GetClassnameByDefinitionIndex(weapon);
        if (!string.IsNullOrEmpty(classname))
            GiveWeapon(player, classname);
    }

    private void AnnounceLastRequestSelected(ILastRequest lastRequest, LastRequestStartContext context)
    {
        var opponent = context.Guard == null
            ? _core.Localizer["last_request_all_guards"]
            : FormatPlayerName(context.Guard);
        var variant = context.SelectedVariant?.Name ?? _core.Localizer["none"];

        _players.SendMessage(MessageType.Chat, "last_request_selected", true, args: [FormatPlayerName(context.Prisoner), opponent, lastRequest.Name, variant]);
    }

    private int RecordLastRequestStats(IJBPlayer? winner, IJBPlayer? loser)
    {
        var winnerWins = 0;

        if (winner != null && PlayerIdentity.UsesSteamKey(winner.Player))
            winnerWins = _statsDB.AddLastRequestWin(winner.SteamID, winner.Player.Name).LastRequestWins;

        if (loser != null && PlayerIdentity.UsesSteamKey(loser.Player) && !LastRequestRules.SamePlayer(loser, winner))
            _statsDB.AddLastRequestLoss(loser.SteamID, loser.Player.Name);

        return winnerWins;
    }

    private void AnnounceLastRequestEnded(ILastRequest lastRequest, IJBPlayer? winner, int winnerWins)
    {
        var winnerName = winner == null ? _core.Localizer["none"] : FormatPlayerName(winner);
        _players.SendMessage(MessageType.Chat, "last_request_ended", true, args: [lastRequest.Name, winnerName, winnerWins]);
    }

    private static bool IsParticipant(ILastRequest lastRequest, LastRequestStartContext context, IJBPlayer player)
    {
        return LastRequestRules.IsParticipant(lastRequest, context, player);
    }

    private static bool ShouldEndOnDeath(LastRequestStartContext context, IJBPlayer victim)
    {
        if (LastRequestRules.SamePlayer(victim, context.Prisoner))
            return true;

        return LastRequestRules.SamePlayer(context.Guard, victim);
    }

    private static bool ShouldEndOnDisconnect(LastRequestStartContext context, IJBPlayer player)
    {
        if (LastRequestRules.SamePlayer(player, context.Prisoner))
            return true;

        return LastRequestRules.SamePlayer(context.Guard, player);
    }

    private static IJBPlayer? GetDisconnectWinner(LastRequestStartContext context, IJBPlayer disconnectedPlayer)
    {
        if (LastRequestRules.SamePlayer(disconnectedPlayer, context.Prisoner))
            return context.Guard;

        return context.Prisoner;
    }

    private static IJBPlayer? GetDeathWinner(LastRequestStartContext context, IJBPlayer victim, IJBPlayer? attacker)
    {
        if (LastRequestRules.SamePlayer(victim, context.Prisoner))
            return context.Guard ?? attacker;

        if (LastRequestRules.SamePlayer(context.Guard, victim))
            return context.Prisoner;

        return attacker;
    }

    private static bool IsAlive(IJBPlayer player)
    {
        return player.Player.IsValid && player.Player.IsAlive;
    }

    private static bool IsEligibleLastRequestPrisoner(IJBPlayer player)
    {
        return IsAlive(player) && !player.IsRebel;
    }

    private static string FormatPlayerName(IJBPlayer player)
    {
        var color = player.Team switch
        {
            JBTeam.Guard => "[blue]",
            JBTeam.Prisoner => "[gold]",
            _ => "[silver]"
        };

        return $"{color}{player.Player.Name}[silver]";
    }

    private static void BlockDamage(ref TakeDamageEntityPreContext ctx)
    {
        var e = ctx.Params;
        e.Info.Damage = 0;
        e.Info.TotalledDamage = 0;
        ctx.SetHookResult(HookResult.Stop);
    }

    private IPlayer? GetPlayerFromEntity(CEntityInstance entity)
    {
        var pawn = entity.As<CCSPlayerPawn>();
        if (pawn.IsValid)
            return _core.PlayerManager.GetPlayerFromPawn(pawn) ?? pawn.ToPlayer();

        return null;
    }

    private static void StripWeapons(IPlayer player)
    {
        var weaponServices = player.PlayerPawn?.WeaponServices;
        if (weaponServices == null)
            return;

        foreach (var weapon in weaponServices.MyValidWeapons.ToList())
        {
            if (weapon?.IsValid == true)
                weaponServices.RemoveWeapon(weapon);
        }
    }

    private static void GiveWeapon(IPlayer player, string weaponName)
    {
        var pawn = player.Pawn;
        if (pawn == null || !pawn.IsValid)
            return;

        pawn.ItemServices?.GiveItem<CBaseEntity>(weaponName);
    }

    private void Unhook(ref Guid? hookId)
    {
        if (!hookId.HasValue)
            return;

        _core.GameEvent.Unhook(hookId.Value);
        hookId = null;
    }
}
