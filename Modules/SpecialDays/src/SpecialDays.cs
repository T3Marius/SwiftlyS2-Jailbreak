using Jailbreak.Contract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
using Tomlyn.Extensions.Configuration;

namespace SpecialDays;

[PluginMetadata(
    Author = "T3Marius",
    Name = "[JB Core] SpecialDays",
    Id = "SpecialDays",
    Version = "0.1.7"
)]
public sealed class Main : BasePlugin
{
    private IJailbreak? _jail;
    public static SDConfig GlobalConfig { get; set; } = new();
    public Main(ISwiftlyCore core) : base(core) { }
    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        _jail = interfaceManager.GetSharedInterface<IJailbreak>(IJailbreak.Key);
        if (_jail == null)
        {
            Core.Logger.LogWarning("Jailbreak api is null, special days will not be registered");
            return;
        }
        if (GlobalConfig.KnifeFight.Enabled)
            _jail.RegisterSpecialDay(new KnifeFightDay(Core, _jail));

        if (GlobalConfig.FreeForAll.Enabled)
            _jail.RegisterSpecialDay(new FreeForAllDay(Core, _jail));

        if (GlobalConfig.Teleport.Enabled)
            _jail.RegisterSpecialDay(new TeleportDay(Core, _jail));

        if (GlobalConfig.HideAndSeek.Enabled)
            _jail.RegisterSpecialDay(new HideAndSeekDay(Core, _jail));

        if (GlobalConfig.War.Enabled)
            _jail.RegisterSpecialDay(new WarDay(Core, _jail));

        if (GlobalConfig.NoScope.Enabled)
            _jail.RegisterSpecialDay(new NoScopeDay(Core, _jail));

        if (GlobalConfig.Scout.Enabled)
            _jail.RegisterSpecialDay(new ScoutDay(Core, _jail));

        if (GlobalConfig.Taser.Enabled)
            _jail.RegisterSpecialDay(new TaserDay(Core, _jail));

        if (GlobalConfig.OneInTheChamber.Enabled)
            _jail.RegisterSpecialDay(new OneInTheChamberDay(Core, _jail));
    }
    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeTomlWithModel<SDConfig>("config.toml", "SpecialDays")
            .Configure(b => b.AddTomlFile("config.toml", false, true));

        ServiceCollection collection = new();
        collection.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<SDConfig>()
            .BindConfiguration("SpecialDays");

        var provider = collection.BuildServiceProvider();
        GlobalConfig = provider.GetRequiredService<IOptions<SDConfig>>().Value;
    }
    public override void Unload()
    {
        if (GlobalConfig.KnifeFight.Enabled)
            _jail?.UnregisterSpecialDay("sd_knife_fight");

        if (GlobalConfig.FreeForAll.Enabled)
            _jail?.UnregisterSpecialDay("sd_free_for_all");

        if (GlobalConfig.Teleport.Enabled)
            _jail?.UnregisterSpecialDay("sd_teleport");

        if (GlobalConfig.HideAndSeek.Enabled)
            _jail?.UnregisterSpecialDay("sd_hide_and_seek");
        
        if (GlobalConfig.War.Enabled)
            _jail?.UnregisterSpecialDay("sd_war");

        if (GlobalConfig.NoScope.Enabled)
            _jail?.UnregisterSpecialDay("sd_no_scope");

        if (GlobalConfig.Scout.Enabled)
            _jail?.UnregisterSpecialDay("sd_scout");

        if (GlobalConfig.Taser.Enabled)
            _jail?.UnregisterSpecialDay("sd_taser");

        if (GlobalConfig.OneInTheChamber.Enabled)
            _jail?.UnregisterSpecialDay("sd_one_in_the_chamber");

    }
}
public sealed class KnifeFightDay : SpecialDayBase
{
    public KnifeFightDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }
    public KnifeFightConfig Config => Main.GlobalConfig.KnifeFight;
    public override string Id => "sd_knife_fight";
    public override string Name => Core.Localizer["knife_fight.name"];
    public override string Description => Core.Localizer["knife_fight.description"];
    public override int StartCountdown => Config.StartCountdown;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override bool AllowAllWeapons => false;
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => SpecialDayWeapons.AllKnives;
    public override bool EnableGunsMenu => false;
    public override IReadOnlyList<ItemDefinitionIndex> GunsMenuWeapons => [];
    public override bool StripWeaponsOnStart => true;
    public override IReadOnlyList<string> GiveWeaponsOnStart => ["weapon_knife"];
    public override bool AllowFriendlyFire => true;

    public override void Start()
    {
    }
    public override void End()
    {
    }
}

public sealed class FreeForAllDay : SpecialDayBase
{
    public FreeForAllDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }
    public FreeForAllConfig Config => Main.GlobalConfig.FreeForAll;
    public override string Id => "sd_free_for_all";
    public override string Name => Core.Localizer["free_for_all.name"];
    public override string Description => Core.Localizer["free_for_all.description"];
    public override int StartCountdown => Config.StartCountdown;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override bool AllowAllWeapons => true;
    public override bool EnableGunsMenu => true;
    public override bool StripWeaponsOnStart => false;
    public override bool AllowFriendlyFire => true;

    public override void Start()
    {
    }
    public override void End()
    {
    }
}

public sealed class TeleportDay : SpecialDayBase
{
    public TeleportDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }
    public TeleportConfig Config => Main.GlobalConfig.Teleport;
    public override string Id => "sd_teleport";
    public override string Name => Core.Localizer["teleport.name"];
    public override string Description => Core.Localizer["teleport.description"];
    public override int StartCountdown => Config.StartCountdown;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override bool AllowAllWeapons => true;
    public override bool EnableGunsMenu => true;
    public override bool StripWeaponsOnStart => false;
    public override bool AllowFriendlyFire => true;

    private Guid? _playerHurtId;

    public override void Start()
    {
        _playerHurtId = Core.GameEvent.HookPost<EventPlayerHurt>(OnPlayerHurt);
    }
    public override void End()
    {
        if (_playerHurtId.HasValue)
        {
            Core.GameEvent.Unhook(_playerHurtId.Value);
            _playerHurtId = null;
        }
    }

    private HookResult OnPlayerHurt(EventPlayerHurt e)
    {
        if (e.UserIdPlayer is not IPlayer victim) return HookResult.Continue;
        if (e.AttackerPlayer is not IPlayer attacker) return HookResult.Continue;
        if (victim == attacker) return HookResult.Continue;

        var victimPawn = victim.PlayerPawn;
        var attackerPawn = attacker.PlayerPawn;

        if (attackerPawn == null || victimPawn == null) return HookResult.Continue;

        var victimPos = victimPawn.AbsOrigin;
        var attackerPos = attackerPawn.AbsOrigin;

        if (victimPos == null || attackerPos == null) return HookResult.Continue;

        attacker.Teleport(victimPos, QAngle.Zero, Vector.Zero);
        victim.Teleport(attackerPos, QAngle.Zero, Vector.Zero);

        return HookResult.Continue;
    }
}

public sealed class HideAndSeekDay : SpecialDayBase
{
    public HideAndSeekDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }
    public HideAndSeekConfig Config => Main.GlobalConfig.HideAndSeek;
    public override string Id => "sd_hide_and_seek";
    public override string Name => Core.Localizer["hide_and_seek.name"];
    public override string Description => Core.Localizer["hide_and_seek.description"];
    public override int StartCountdown => Config.HideTime;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.Guards;
    public override bool AllowAllWeapons => false;
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => SpecialDayWeapons.AllKnives;
    public override bool EnableGunsMenu => false;
    public override IReadOnlyList<string> GiveWeaponsOnStart => ["weapon_knife"];
    public override bool StripWeaponsOnStart => true;
    public override bool AllowFriendlyFire => false;

    private bool _damageHooked;

    public override void PreStart()
    {
        if (_damageHooked)
            return;

        Core.Event.OnEntityTakeDamage += OnTakeDamage;
        _damageHooked = true;
    }

    public override void Start()
    {
        SetPrisonersMoveType(MoveType_t.MOVETYPE_OBSOLETE);
    }

    public override void End()
    {
        SetPrisonersMoveType(MoveType_t.MOVETYPE_WALK);

        if (_damageHooked)
        {
            Core.Event.OnEntityTakeDamage -= OnTakeDamage;
            _damageHooked = false;
        }
    }

    private void OnTakeDamage(IOnEntityTakeDamageEvent e)
    {
        var rawVictim = GetPlayerFromEntity(e.Entity);
        if (rawVictim == null)
            return;

        var victim = Jailbreak.Players.SyncPlayer(rawVictim);
        if (victim == null || victim.Team != JBTeam.Guard)
            return;

        e.Info.Damage = 0;
        e.Info.TotalledDamage = 0;
        e.DamageResult.DamageDealt = 0;
        e.Result = HookResult.Stop;
    }

    private void SetPrisonersMoveType(MoveType_t moveType)
    {
        foreach (var prisoner in Jailbreak.Players.GetPlayersByTeam(JBTeam.Prisoner))
        {
            var pawn = prisoner.Player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                continue;

            if (pawn.MoveType == moveType && pawn.ActualMoveType == moveType)
                continue;

            pawn.MoveType = moveType;
            pawn.ActualMoveType = moveType;
            pawn.MoveTypeUpdated();
        }
    }

    private IPlayer? GetPlayerFromEntity(CEntityInstance entity)
    {
        var pawn = entity.As<CCSPlayerPawn>();
        if (pawn.IsValid)
        {
            var playerFromPawn = Core.PlayerManager.GetPlayerFromPawn(pawn);
            if (playerFromPawn != null)
                return playerFromPawn;

            var player = pawn.ToPlayer();
            if (player != null)
                return player;
        }
        return null;
    }
}

public sealed class WarDay : SpecialDayBase
{
    public WarDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public WarConfig Config => Main.GlobalConfig.War;
    public override string Id => "sd_war";
    public override string Name => Core.Localizer["war.name"];
    public override string Description => Core.Localizer["war.description"];
    public override int StartCountdown => Config.PrepareTime;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.Prisoners;
    public override bool AllowAllWeapons => true;
    public override bool EnableGunsMenu => true;
    public override bool StripWeaponsOnStart => false;
    public override bool AllowFriendlyFire => false;

    public override void Start()
    {
    }

    public override void End()
    {
    }
}

public sealed class NoScopeDay : SpecialDayBase
{
    private static readonly ItemDefinitionIndex[] SniperWeapons = SpecialDayWeapons.Snipers.ToArray();

    public NoScopeDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public NoScopeConfig Config => Main.GlobalConfig.NoScope;
    public override string Id => "sd_no_scope";
    public override string Name => Core.Localizer["no_scope.name"];
    public override string Description => Core.Localizer["no_scope.description"];
    public override int StartCountdown => Config.StartCountdown;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => SpecialDayWeapons.Snipers;
    public override IReadOnlyList<ItemDefinitionIndex> GunsMenuWeapons => SniperWeapons;
    public override bool StripWeaponsOnStart => true;
    public override bool AllowFriendlyFire => true;

    public override void Start()
    {
        Core.Event.OnTick += OnTick;

        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || !player.IsAlive)
                continue;

            GiveRandomSniper(player);
        }
    }

    public override void End()
    {
        Core.Event.OnTick -= OnTick;
    }

    private void GiveRandomSniper(IPlayer player)
    {
        if (SniperWeapons.Length == 0)
            return;

        var weapon = SniperWeapons[Random.Shared.Next(SniperWeapons.Length)];
        var classname = Core.Helpers.GetClassnameByDefinitionIndex(weapon);
        if (string.IsNullOrEmpty(classname))
            return;

        player.Pawn?.ItemServices?.GiveItem<CBaseEntity>(classname);
    }

    private void OnTick()
    {
        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || !player.IsAlive)
                continue;

            var activeWeapon = player.Pawn?.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon == null || !activeWeapon.IsValid)
                continue;

            activeWeapon.NextSecondaryAttackTick.Value = Core.Engine.GlobalVars.TickCount + 500;
            activeWeapon.NextSecondaryAttackTickUpdated();
        }
    }
}

public sealed class ScoutDay : SpecialDayBase
{
    private const string ScoutClassname = "weapon_ssg08";

    public ScoutDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public ScoutConfig Config => Main.GlobalConfig.Scout;
    public override string Id => "sd_scout";
    public override string Name => Core.Localizer["scout.name"];
    public override string Description => Core.Localizer["scout.description", Config.Gravity];
    public override int StartCountdown => Config.StartCountdown;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => SpecialDayWeapons.Snipers;
    public override IReadOnlyList<ItemDefinitionIndex> GunsMenuWeapons => [ItemDefinitionIndex.Ssg08];
    public override bool StripWeaponsOnStart => true;
    public override IReadOnlyList<string> GiveWeaponsOnStart => [ScoutClassname];
    public override bool AllowFriendlyFire => true;

    public override void Start()
    {
        SetPlayersGravity(Config.Gravity);
    }

    public override void End()
    {
        SetPlayersGravity(1f);
    }

    private void SetPlayersGravity(float gravity)
    {
        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || !player.IsAlive)
                continue;

            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                continue;

            pawn.GravityScale = gravity;
            pawn.ActualGravityScale = gravity;
            pawn.GravityScaleUpdated();
        }
    }
}
public sealed class TaserDay : SpecialDayBase
{
    public TaserDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public override string Id => "sd_taser";
    public override string Name => Core.Localizer["taser.name"];
    public override string Description => Core.Localizer["taser.description"];
    public override int StartCountdown => Main.GlobalConfig.Taser.StartCountdown;

    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override bool AllowAllWeapons => false;
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => new HashSet<ItemDefinitionIndex>
    {
        ItemDefinitionIndex.Taser
    };

    public override bool EnableGunsMenu => false;
    public override bool StripWeaponsOnStart => true;
    public override IReadOnlyList<string> GiveWeaponsOnStart => ["weapon_taser"];
    public override bool AllowFriendlyFire => true;

    public override void Start() { }
    public override void End() { }
}
public sealed class OneInTheChamberDay : SpecialDayBase
{
    public OneInTheChamberDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public override string Id => "sd_one_in_the_chamber";
    public override string Name => Core.Localizer["one_in_the_chamber.name"];
    public override string Description => Core.Localizer["one_in_the_chamber.description"];
    public override int StartCountdown => Main.GlobalConfig.OneInTheChamber.StartCountdown;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override bool AllowAllWeapons => false;
    private ItemDefinitionIndex? GetConfiguredOitcWeapon()
    {
        foreach (var weapon in SpecialDayWeapons.GunsMenuWeapons)
        {
            var classname = Core.Helpers.GetClassnameByDefinitionIndex(weapon);

            if (string.Equals(classname, Main.GlobalConfig.OneInTheChamber.OitcGun, StringComparison.OrdinalIgnoreCase))
                return weapon;
        }
        return null;
    }
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons
    {
        get
        {
            var allowed = SpecialDayWeapons.AllKnives.ToHashSet();

            var weapon = GetConfiguredOitcWeapon();
            if (weapon.HasValue)
                allowed.Add(weapon.Value);

            return allowed;
        }
    }
    public override bool StripWeaponsOnStart => true;
    public override IReadOnlyList<string> GiveWeaponsOnStart => [""];
    public override bool EnableGunsMenu => false;
    public override bool AllowFriendlyFire => true;

    private Guid? _playerDeathId;

    public override void Start()
    {
        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid)
                continue;
            GiveWeapon(player, Main.GlobalConfig.OneInTheChamber.OitcGun);
            Core.Scheduler.NextWorldUpdate(() =>
            {
                SetAmmo(player, 1, 0); 
            });
        }

        Core.Event.OnEntityTakeDamage += OnTakeDamage;
        _playerDeathId = Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
    }
    public override void End()
    {
        Core.Event.OnEntityTakeDamage -= OnTakeDamage;
        if (_playerDeathId.HasValue)
        {
            Core.GameEvent.Unhook(_playerDeathId.Value);
        }
    }
    private void OnTakeDamage(IOnEntityTakeDamageEvent e)
    {
        if (!e.Entity.DesignerName.Contains("player"))
            return;

        CCSPlayerPawn? attackerPawn;
        try
        {
            attackerPawn = e.Info.AttackerInfo.AttackerPawn.Value;
        }
        catch (NullReferenceException)
        {
            return;
        }

        if (attackerPawn == null || !attackerPawn.IsValid)
            return;

        if (attackerPawn.WeaponServices == null)
            return;

        CBasePlayerWeapon? activeWeapon;
        try
        {
            activeWeapon = attackerPawn.WeaponServices.ActiveWeapon.Value;
        }
        catch (NullReferenceException)
        {
            return;
        }

        if (activeWeapon == null)
            return;

        var player = attackerPawn.ToPlayer();
        if (player == null)
            return;

        if (activeWeapon.DesignerName == Main.GlobalConfig.OneInTheChamber.OitcGun)
        {
            IncrementPlayerAmmo(player);
            e.Info.Damage = 9999;
        }
    }
    private HookResult OnPlayerDeath(EventPlayerDeath e)
    {
        if (e.AttackerPlayer is not IPlayer player)
            return HookResult.Continue;

        if (!e.Weapon.Contains("knife")) // only check this event for knife deaths
        {
            return HookResult.Continue;
        }
        IncrementPlayerAmmo(player);

        return HookResult.Continue;
    }
    private void SetAmmo(IPlayer player, int ammo, int reserve)
    {
        var pawn = player.Pawn;
        if (pawn == null || !pawn.IsValid)
            return;

        if (pawn.WeaponServices == null)
            return;

        // set ammo for all weapons
        foreach (var weapon in pawn.WeaponServices.MyValidWeapons)
        {
            weapon.Clip1          = ammo;
            weapon.Clip2          = reserve;
            weapon.ReserveAmmo[0] = reserve;

            weapon.Clip1Updated();
            weapon.Clip2Updated();
            weapon.ReserveAmmoUpdated();
        }
    }
    private void GiveWeapon(IPlayer player, string weapon_name)
    {
        if (player.Pawn == null)
            return;

        if (player.Pawn.ItemServices == null)
            return;

        player.Pawn.ItemServices.GiveItem<CBaseEntity>(weapon_name);
        player.Pawn.ItemServices.GiveItem<CBaseEntity>(player.Controller.Team == Team.T ? "weapon_knife_t" : "weapon_knife");
    }
    private void IncrementPlayerAmmo(IPlayer player)
    {
        var pawn = player.Pawn;
        if (pawn == null || pawn.WeaponServices == null)
            return;

        foreach (var weapon in pawn.WeaponServices.MyValidWeapons)
        {
            weapon.Clip1 += 1;
            weapon.Clip2 += 1;
            weapon.Clip1Updated();
            weapon.Clip2Updated();
        }
    }
}
