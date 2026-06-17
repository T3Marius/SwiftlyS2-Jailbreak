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

namespace LastRequests;

[PluginMetadata(
    Author = "T3Marius",
    Name = "[JB Core] LastRequests",
    Id = "LastRequests",
    Version = "0.1.1"
)]
public sealed class Main : BasePlugin
{
    private IJailbreak? _jail;
    public static LRConfig GlobalConfig { get; set; } = new();
    public Main(ISwiftlyCore core) : base(core)
    {
    }

    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        _jail = interfaceManager.GetSharedInterface<IJailbreak>(IJailbreak.Key);
        if (_jail == null)
        {
            Core.Logger.LogWarning("Jailbreak api is null, last requests will not be registered");
            return;
        }

        if (GlobalConfig.KnifeFight.Enabled)
            _jail.RegisterLastRequest(new KnifeFightLastRequest(Core, _jail));
        
        if (GlobalConfig.ShotForShot.Enabled)
            _jail.RegisterLastRequest(new ShotForShotLastRequest(Core, _jail));
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeTomlWithModel<LRConfig>("config.toml", "LastRequests")
            .Configure(builder => builder.AddTomlFile("config.toml"));

        ServiceCollection collection = new();
        collection.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<LRConfig>()
            .BindConfiguration("LastRequests");

        var provider = collection.BuildServiceProvider();
        GlobalConfig = provider.GetRequiredService<IOptions<LRConfig>>().Value;
    }

    public override void Unload()
    {
        if (GlobalConfig.KnifeFight.Enabled)
            _jail?.UnregisterLastRequest("lr_knife_fight");

        if (GlobalConfig.ShotForShot.Enabled)
            _jail?.UnregisterLastRequest("lr_shot_for_shot");
    }
}

public sealed class KnifeFightLastRequest : LastRequestBase
{
    private KnifeFightConfig Config => Main.GlobalConfig.KnifeFight;
    private const string NormalVariant = "normal";
    private const string GravityVariant = "gravity";
    private const string SpeedVariant = "speed";
    private const string OneHitVariant = "one_hit";
    private const float OneHitDamage = 9999f;


    private bool _oneHitHooked;
    private readonly HashSet<ulong> _modifiedPlayers = [];

    public KnifeFightLastRequest(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail)
    {
    }

    public override string Id => "lr_knife_fight";
    public override string Name => Core.Localizer["knife_fight.name"];
    public override string Description => Core.Localizer["knife_fight.description"];
    public override int StartCountdown => Config.Countdown;
    public override LastRequestOpponentMode OpponentMode => LastRequestOpponentMode.Duel;
    public override LastRequestWeaponSelection WeaponSelection => LastRequestWeaponSelection.None;
    public override bool AllowAllWeapons => false;
    public override IReadOnlySet<ItemDefinitionIndex> AllowedWeapons => LastRequestWeapons.AllKnives;
    public override IReadOnlyList<ItemDefinitionIndex> WeaponMenuWeapons => [];
    public override IReadOnlyList<LastRequestVariant> Variants =>
    [
        new(NormalVariant, "Normal", "Classic knife fight."),
        new(GravityVariant, "Gravity", "Classic knife fight with lower gravity."),
        new(SpeedVariant, "Speed", "Classic knife fight with extra speed."),
        new(OneHitVariant, "OneHit", "Every knife hit is lethal.")
    ];
    public override bool StripWeaponsOnStart => true;
    public override IReadOnlyList<string> GiveWeaponsOnStart => ["weapon_knife"];

    public override void Start(LastRequestStartContext context)
    {
        base.Start(context);

        switch (context.SelectedVariant?.Id)
        {
            case GravityVariant:
                ApplyToPlayers(player => SetGravity(player, Config.GravityTypeValue));
                break;
            case SpeedVariant:
                ApplyToPlayers(player => SetSpeed(player, Config.SpeedTypeValue));
                break;
            case OneHitVariant:
                HookOneHit();
                break;
        }
    }

    public override void End(IJBPlayer? winner, IJBPlayer? loser)
    {
        UnhookOneHit();
        RestoreModifiedPlayers();
        base.End(winner, loser);
    }

    private void ApplyToPlayers(Action<IJBPlayer> action)
    {
        action(Prisoner);

        if (Guard != null)
            action(Guard);
    }

    private void SetGravity(IJBPlayer player, float scale)
    {
        _modifiedPlayers.Add(player.SteamID);
        Core.Scheduler.NextWorldUpdate(() =>
        {
            var pawn = player.Player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                return;

            pawn.GravityScale = scale;
            pawn.ActualGravityScale = scale;
            pawn.GravityScaleUpdated();
        });
    }

    private void SetSpeed(IJBPlayer player, float scale)
    {
        _modifiedPlayers.Add(player.SteamID);
        Core.Scheduler.NextWorldUpdate(() =>
        {
            var pawn = player.Player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                return;

            pawn.VelocityModifier = scale;
            pawn.VelocityModifierUpdated();
        });
    }

    private void RestoreModifiedPlayers()
    {
        foreach (var steamId in _modifiedPlayers.ToArray())
        {
            var player = Jailbreak.Players.GetAllPlayers().FirstOrDefault(p => p.SteamID == steamId);
            if (player == null)
                continue;

            Core.Scheduler.NextWorldUpdate(() =>
            {
                var pawn = player.Player.PlayerPawn;
                if (pawn == null || !pawn.IsValid)
                    return;

                pawn.GravityScale = 1f;
                pawn.ActualGravityScale = 1f;
                pawn.GravityScaleUpdated();
                pawn.VelocityModifier = 1f;
                pawn.VelocityModifierUpdated();
            });
        }

        _modifiedPlayers.Clear();
    }

    private void HookOneHit()
    {
        if (_oneHitHooked)
            return;

        Core.Event.OnEntityTakeDamage += OnEntityTakeDamage;
        _oneHitHooked = true;
    }

    private void UnhookOneHit()
    {
        if (!_oneHitHooked)
            return;

        Core.Event.OnEntityTakeDamage -= OnEntityTakeDamage;
        _oneHitHooked = false;
    }

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

        var attacker = Jailbreak.Players.SyncPlayer(rawAttacker);
        var victim = Jailbreak.Players.SyncPlayer(rawVictim);

        if (attacker == null || victim == null || !IsParticipant(attacker) || !IsParticipant(victim))
            return;

        if (!IsKnifeDamage(attacker, e.Info))
            return;

        e.Info.Damage = OneHitDamage;
        e.Info.TotalledDamage = OneHitDamage;
        e.DamageResult.DamageDealt = OneHitDamage;
    }

    private bool IsParticipant(IJBPlayer player)
    {
        return player.SteamID == Prisoner.SteamID
            || Guard?.SteamID == player.SteamID;
    }

    private static bool IsKnifeDamage(IJBPlayer attacker, CTakeDamageInfo info)
    {
        var ability = info.Ability.Value;
        if (ability != null && ability.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase))
            return true;

        var activeWeapon = attacker.Player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value;
        return activeWeapon != null
            && activeWeapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase);
    }

    private IPlayer? GetPlayerFromEntity(CEntityInstance entity)
    {
        var pawn = entity.As<CCSPlayerPawn>();
        if (pawn.IsValid)
            return Core.PlayerManager.GetPlayerFromPawn(pawn) ?? pawn.ToPlayer();

        return null;
    }
}
public sealed class ShotForShotLastRequest : LastRequestBase
{
    public ShotForShotLastRequest(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public override string Id => "lr_shot_for_shot";
    public override string Name => Core.Localizer["shot_for_shot.name"];
    public override string Description => Core.Localizer["shot_for_shot.description"];
    public override bool AllowAllWeapons => true;
    public override LastRequestOpponentMode OpponentMode => LastRequestOpponentMode.Duel;
    public override int StartCountdown => Main.GlobalConfig.ShotForShot.StartCountdown;
    public override bool RequiresWeaponSelection => true;
    public override bool RequiresVariantSelection => false;
    public override LastRequestWeaponSelection WeaponSelection => LastRequestWeaponSelection.Required;
    
    private IJBPlayer? _prisoner;
    private IJBPlayer? _guard;

    private Guid? _weaponFire;
    private Random _random = new();

    public override void Start(LastRequestStartContext context)
    {
        if (context.Guard == null)
            return;

        _guard      = context.Guard;
        _prisoner   = context.Prisoner;
        
        _weaponFire = Core.GameEvent.HookPost<EventWeaponFire>(OnWeaponFire);

        int random  = _random.Next(0, 2);

        Core.Scheduler.NextWorldUpdate(() =>
        {            
            if (random == 0)
            {
                SetAmmo(context.Prisoner, 1);
                SetAmmo(context.Guard, 0);
            }
            else if (random == 1)
            {
                SetAmmo(context.Guard, 1);
                SetAmmo(context.Prisoner, 0);
            }
        });
    }
    public override void End(IJBPlayer? winner, IJBPlayer? loser)
    {
        if (_weaponFire.HasValue)
        {
            Core.GameEvent.Unhook(_weaponFire.Value);
        }
    }
    private HookResult OnWeaponFire(EventWeaponFire e)
    {
        if (e.UserIdPlayer is not IPlayer shooterSender)
            return HookResult.Continue;

        var shooter = Jailbreak.Players.SyncPlayer(shooterSender);
        if (shooter == null || !shooter.Player.IsValid)
            return HookResult.Continue;

        if (_guard == null || _prisoner == null)
            return HookResult.Continue;

        if (shooter == _guard)
        {
            SetAmmo(_prisoner, 1);
        }
        else if (shooter == _prisoner)
        {
            SetAmmo(_guard, 1);
        }

        return HookResult.Continue;
    }

    private void SetAmmo(IJBPlayer player, int ammo)
    {
        var pawn = player.Player.Pawn;
        if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null)
            return;

        foreach (var weapon in pawn.WeaponServices.MyValidWeapons)
        {
            if (!weapon.IsValid)
                continue;

            weapon.Clip1 = ammo;
            weapon.ReserveAmmo[0] = 0;
            weapon.Clip1Updated();
            weapon.ReserveAmmoUpdated();
        }
    }
}