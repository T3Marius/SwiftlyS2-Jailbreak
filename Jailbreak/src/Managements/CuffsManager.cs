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

public sealed class CuffsManager
{
    private readonly ISwiftlyCore        _core;
    private readonly IJBPlayerManagement _players;

    private const string CuffWeapon   = "weapon_taser";
    private const float GrabDistance  = 110.0f;
    private const float TraceDistance = 350.0f;

    private readonly Dictionary<ulong, ulong> _cuffedByWarden    = [];
    private readonly Dictionary<ulong, ulong> _grabbedByWarden   = [];
    private readonly HashSet<ulong>           _wardenTaserOwners = [];
    private readonly HashSet<ulong>           _mouse2HeldWardens = [];
    
    public CuffsManager(ISwiftlyCore core, IJBPlayerManagement players)
    {
        _core = core;
        _players = players;
    }

    public void OnWardenGive(IJBPlayer warden)
    {
        if (!warden.IsWarden || !warden.Player.IsValid)
            return;

        _wardenTaserOwners.Add(warden.SteamID);
        PlayerUtils.GiveWeapon(warden.Player, "weapon_taser", _core.Scheduler);
        GiveInfiniteTaserAmmo(warden);
    }
    
    public void OnWardenRemove(IJBPlayer warden)
    {
        _wardenTaserOwners.Remove(warden.SteamID);
        _mouse2HeldWardens.Remove(warden.SteamID);
        _grabbedByWarden.Remove(warden.SteamID);

        foreach (var prisonerSteamId in _cuffedByWarden
                    .Where(x => x.Value == warden.SteamID)
                    .Select(x => x.Key)
                    .ToList())
        {
            var prisoner = _players.GetAllPlayers().FirstOrDefault(p => p.SteamID == prisonerSteamId);
            if (prisoner != null)
                Uncuff(prisoner);
            else
                _cuffedByWarden.Remove(prisonerSteamId);
        }
    }

    public void CleanupPlayer(ulong steamId)
    {
        _wardenTaserOwners.Remove(steamId);
        _mouse2HeldWardens.Remove(steamId);
        _grabbedByWarden.Remove(steamId);

        if (_cuffedByWarden.ContainsKey(steamId))
            _cuffedByWarden.Remove(steamId);

        foreach (var prisonerSteamId in _cuffedByWarden
                     .Where(x => x.Value == steamId)
                     .Select(x => x.Key)
                     .ToList())
        {
            var prisoner = _players.GetAllPlayers().FirstOrDefault(p => p.SteamID == prisonerSteamId);
            if (prisoner != null)
                Uncuff(prisoner);
            else
                _cuffedByWarden.Remove(prisonerSteamId);
        }
    }

    public void CleanupAll()
    {
        foreach (var prisoner in _players.GetAllPlayers().Where(p => p.IsCuffed).ToList())
            Uncuff(prisoner);

        _cuffedByWarden.Clear();
        _grabbedByWarden.Clear();
        _wardenTaserOwners.Clear();
        _mouse2HeldWardens.Clear();
    }

    [EventListener<EventDelegates.OnClientKeyStateChanged>]
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

        if (!e.Pressed)
        {
            _mouse2HeldWardens.Remove(player.SteamID);
            _grabbedByWarden.Remove(player.SteamID);
            return;
        }

        if (player.IsWarden)
            _mouse2HeldWardens.Add(player.SteamID);
    }

    [EventListener<EventDelegates.OnTick>]
    private void OnTick()
    {
        foreach (var prisoner in _players.GetAllPlayers().Where(p => p.IsCuffed))
            PlayerUtils.FreezeVelocity(prisoner.Player, new Color(0, 255, 0, 255));

        foreach (var wardenSteamId in _mouse2HeldWardens.ToList())
        {
            var warden = _players.GetAllPlayers().FirstOrDefault(p => p.SteamID == wardenSteamId);
            if (warden == null || !warden.IsWarden || !warden.Player.IsValid || !warden.Player.IsAlive)
            {
                ReleaseGrab(wardenSteamId);
                continue;
            }

            if ((warden.Player.PressedButtons & GameButtonFlags.Mouse2) == 0)
            {
                ReleaseGrab(wardenSteamId);
                continue;
            }

            if (_grabbedByWarden.TryGetValue(wardenSteamId, out var grabbedSteamId))
            {
                var grabbed = _players.GetAllPlayers().FirstOrDefault(p => p.SteamID == grabbedSteamId);
                if (grabbed == null || !grabbed.IsCuffed || !grabbed.Player.IsAlive)
                {
                    _grabbedByWarden.Remove(wardenSteamId);
                    continue;
                }

                MoveGrabbedPrisoner(warden, grabbed);
                continue;
            }

            var target = FindLookedAtCuffedPrisoner(warden);
            if (target == null)
                continue;

            _grabbedByWarden[wardenSteamId] = target.SteamID;
            MoveGrabbedPrisoner(warden, target);
        }
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult EventWeaponFire(EventWeaponFire e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        if (!e.Weapon.Equals("taser", StringComparison.OrdinalIgnoreCase)
            && !e.Weapon.Equals(CuffWeapon, StringComparison.OrdinalIgnoreCase))
            return HookResult.Continue;

        var player = _players.SyncPlayer(e.UserIdPlayer);
        if (player == null || !player.IsWarden)
            return HookResult.Continue;

        GiveInfiniteTaserAmmo(player);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult EventPlayerDeath(EventPlayerDeath e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        var victim = _players.SyncPlayer(e.UserIdPlayer);
        if (victim == null || !victim.IsCuffed)
            return HookResult.Continue;

        Uncuff(victim);

        return HookResult.Continue;
    }

    [EventListener<EventDelegates.OnEntityTakeDamage>]
    private void OnEntityTakeDamage(IOnEntityTakeDamageEvent e)
    {
        if (!e.Entity.DesignerName.StartsWith("player", StringComparison.OrdinalIgnoreCase))
            return;

        var attackerPawn = e.Info.AttackerInfo.AttackerPawn.Value;
        if (attackerPawn == null)
            return;

        var rawAttacker = attackerPawn.ToPlayer();
        var rawVictim = GetPlayerFromEntity(e.Entity);

        if (rawAttacker == null || rawVictim == null)
            return;

        var attacker = _players.SyncPlayer(rawAttacker);
        var victim   = _players.SyncPlayer(rawVictim);

        if (attacker == null || victim == null)
            return;

        if (!attacker.IsWarden || victim.Team != JBTeam.Prisoner)
            return;

        if (!IsTaserDamage(attacker, e.Info))
            return;

        e.Info.Damage = 0;
        e.Info.TotalledDamage = 0;
        e.DamageResult.DamageDealt = 0;

        if (victim.IsCuffed)
            Uncuff(victim);
        else
            Cuff(victim, attacker);

        e.Result = HookResult.Stop;
    }

    private void Cuff(IJBPlayer prisoner, IJBPlayer warden)
    {
        prisoner.IsCuffed = true;
        _cuffedByWarden[prisoner.SteamID] = warden.SteamID;

        PlayerUtils.FreezeVelocity(prisoner.Player, new Color(0, 255, 0, 255));
    }

    private void Uncuff(IJBPlayer prisoner)
    {
        prisoner.IsCuffed = false;
        _cuffedByWarden.Remove(prisoner.SteamID);

        foreach (var pair in _grabbedByWarden.Where(x => x.Value == prisoner.SteamID).ToList())
            _grabbedByWarden.Remove(pair.Key);

        var color = prisoner.IsRebel
            ? new Color(255, 0, 0, 255)
            : new Color(255, 255, 255, 255);

        PlayerUtils.UnfreezeVelocity(prisoner.Player, color);
    }

    private bool IsTaserDamage(IJBPlayer attacker, CTakeDamageInfo info)
    {
        if (!_wardenTaserOwners.Contains(attacker.SteamID))
            return false;

        var ability = info.Ability.Value;
        if (ability != null && ability.DesignerName.Equals(CuffWeapon, StringComparison.OrdinalIgnoreCase))
            return true;

        var activeWeapon = attacker.Player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value;
        return activeWeapon != null
            && activeWeapon.DesignerName.Equals(CuffWeapon, StringComparison.OrdinalIgnoreCase);
    }

    private void GiveInfiniteTaserAmmo(IJBPlayer warden)
    {
        _core.Scheduler.NextWorldUpdate(() =>
        {
            var taser = FindTaser(warden.Player);
            if (taser == null)
            {
                PlayerUtils.GiveWeapon(warden.Player, CuffWeapon, _core.Scheduler);
                _core.Scheduler.NextWorldUpdate(() => RefillTaser(warden.Player));
                return;
            }

            RefillTaser(warden.Player);
        });
    }

    private void RefillTaser(IPlayer player)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return;

        var taser = FindTaser(player);
        if (taser == null)
            return;

        taser.Clip1 = 1;
        taser.Clip2 = 1;

        for (var i = 0; i < taser.ReserveAmmo.ElementCount; i++)
            taser.ReserveAmmo[i] = 1;

        taser.Clip1Updated();
        taser.Clip2Updated();
        taser.ReserveAmmoUpdated();

        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null)
            return;

        for (var i = 0; i < weaponServices.Ammo.ElementCount; i++)
            weaponServices.Ammo[i] = 1;

        weaponServices.AmmoUpdated();
    }

    private CBasePlayerWeapon? FindTaser(IPlayer player)
    {
        return player.PlayerPawn?.WeaponServices?.MyValidWeapons
            .FirstOrDefault(w => w.DesignerName.Equals(CuffWeapon, StringComparison.OrdinalIgnoreCase));
    }

    private IJBPlayer? FindLookedAtCuffedPrisoner(IJBPlayer warden)
    {
        var pawn = warden.Player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return null;

        var start = GetEyePosition(warden.Player);

        var options = TraceParams.Builder()
            .WithLineRay()
            .WithIterateEntities(true)
            .WithInteraction(MaskTrace.Player | MaskTrace.Solid | MaskTrace.Hitbox, MaskTrace.Empty, MaskTrace.Player)
            .WithCollisionGroup(CollisionGroup.PlayerMovement)
            .WithObjectQuery(RnQueryObjectSet.AllGameEntities)
            .WithShouldHitEntity(entity => entity.Address != pawn.Address)
            .HitSolid()
            .Build();

        var trace = _core.Trace.TraceShapeAngle(start, pawn.EyeAngles, TraceDistance, options);
        if (!trace.HitPlayer(out var rawTarget) || rawTarget == null)
            return null;

        var target = _players.SyncPlayer(rawTarget);
        if (target == null || !target.IsCuffed)
            return null;

        return target;
    }

    private void MoveGrabbedPrisoner(IJBPlayer warden, IJBPlayer prisoner)
    {
        var pawn = warden.Player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return;

        pawn.EyeAngles.ToDirectionVectors(out var forward, out _, out _);

        var start = GetEyePosition(warden.Player);
        var pos = start + new Vector(
            forward.X * GrabDistance,
            forward.Y * GrabDistance,
            forward.Z * GrabDistance - 35.0f
        );

        prisoner.Player.Teleport(pos: pos, velocity: Vector.Zero);
    }

    private Vector GetEyePosition(IPlayer player)
    {
        var origin = player.PlayerPawn?.AbsOrigin ?? Vector.Zero;
        return origin + new Vector(0, 0, 64);
    }

    private void ReleaseGrab(ulong wardenSteamId)
    {
        _mouse2HeldWardens.Remove(wardenSteamId);
        _grabbedByWarden.Remove(wardenSteamId);
    }

    private IPlayer? GetPlayerFromEntity(CEntityInstance entity)
    {
        var pawn = entity.As<CCSPlayerPawn>();
        if (pawn.IsValid)
        {
            var player = pawn.ToPlayer();
            if (player != null)
                return player;
        }

        return GetPlayerFromEntityAddress(entity.Address);
    }

    private IPlayer? GetPlayerFromEntityAddress(nint address)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player.PlayerPawn?.Address == address)
                return player;
        }

        return null;
    }
}
