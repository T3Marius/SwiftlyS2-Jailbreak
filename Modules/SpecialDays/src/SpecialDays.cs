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
    Version = "0.1.4"
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
    public override void PreStart()
    {
        Core.Event.OnEntityTakeDamage += OnTakeDamage;
    }
    public override void Start()
    {
        SetPrisonersMoveType(MoveType_t.MOVETYPE_OBSOLETE);
    }
    public override void End()
    {
        SetPrisonersMoveType(MoveType_t.MOVETYPE_WALK);
        Core.Event.OnEntityTakeDamage -= OnTakeDamage;
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
    // it's so easy to make special days like hns and war with this base :))
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

    public override void PreStart()
    {
        
    }
    public override void Start()
    {
    }
    public override void End()
    {
    }
}
public sealed class NoScopeDay : SpecialDayBase
{
    public NoScopeDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }
    public NoScopeConfig Config => Main.GlobalConfig.NoScope;
    public override string Id => "sd_no_scope";
    public override string Name => Core.Localizer["no_scope.name"];
    public override string Description => Core.Localizer["no_scope.description"];
    public override int StartCountdown => Config.StartCountdown;
    public override SpecialDayFreezeTeam FreezeTeamOnCountdown => SpecialDayFreezeTeam.None;
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => SpecialDayWeapons.Snipers;
    public override bool StripWeaponsOnStart => true;
    public override bool AllowFriendlyFire => true;
    public List<string> Snipers { get; set; } = ["weapon_awp", "weapon_ssg08", "weapon_scar20", "weapon_g3sg1"];
    public override void PreStart()
    {
        Core.Event.OnTick += OnTick;
    }
    public override void Start()
    {
        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid)
                continue;

            var randomIndex = new Random().Next(Snipers.Count);
            player.Pawn?.ItemServices?.GiveItem<CBaseEntity>(Snipers[randomIndex]);
        }
    }
    public override void End()
    {
        Core.Event.OnTick -= OnTick;
    }
    private void OnTick()
    {
        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid)
                continue;
            
            var activeWeapon = player.Pawn?.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon == null || !activeWeapon.IsValid)
                continue;

            activeWeapon.NextPrimaryAttackTick.Value = 999;
            activeWeapon.NextPrimaryAttackTickRatio  = 999;
            activeWeapon.NextPrimaryAttackTickUpdated();
            activeWeapon.NextPrimaryAttackTickRatioUpdated();
        }
    }
        
}
