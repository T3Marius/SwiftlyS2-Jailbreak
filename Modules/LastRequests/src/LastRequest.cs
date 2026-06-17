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
    Version = "0.1.2"
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

        if (GlobalConfig.MagForMag.Enabled)
            _jail.RegisterLastRequest(new MagForMagLastRequest(Core, _jail));
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

        if (GlobalConfig.MagForMag.Enabled)
            _jail?.UnregisterLastRequest("lr_mag_for_mag");
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
        _guard    = null;
        _prisoner = null;
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
public sealed class MagForMagLastRequest : LastRequestBase
{
    private static readonly IReadOnlyDictionary<ItemDefinitionIndex, int> MagazineSizes = new Dictionary<ItemDefinitionIndex, int>
    {
        [ItemDefinitionIndex.Deagle] = 7,
        [ItemDefinitionIndex.Elite] = 30,
        [ItemDefinitionIndex.FiveSeven] = 20,
        [ItemDefinitionIndex.Glock] = 20,
        [ItemDefinitionIndex.Tec9] = 18,
        [ItemDefinitionIndex.HkP2000] = 13,
        [ItemDefinitionIndex.UspSilencer] = 12,
        [ItemDefinitionIndex.Cz75a] = 12,
        [ItemDefinitionIndex.Revolver] = 8,
        [ItemDefinitionIndex.Ak47] = 30,
        [ItemDefinitionIndex.Aug] = 30,
        [ItemDefinitionIndex.Famas] = 25,
        [ItemDefinitionIndex.Sg556] = 30,
        [ItemDefinitionIndex.Galilar] = 35,
        [ItemDefinitionIndex.M4a1] = 30,
        [ItemDefinitionIndex.M4a1Silencer] = 20,
        [ItemDefinitionIndex.Mac10] = 30,
        [ItemDefinitionIndex.P90] = 50,
        [ItemDefinitionIndex.Mp5sd] = 30,
        [ItemDefinitionIndex.Ump45] = 25,
        [ItemDefinitionIndex.Bizon] = 64,
        [ItemDefinitionIndex.Mp7] = 30,
        [ItemDefinitionIndex.Mp9] = 30,
        [ItemDefinitionIndex.Awp] = 5,
        [ItemDefinitionIndex.G3sg1] = 20,
        [ItemDefinitionIndex.Scar20] = 20,
        [ItemDefinitionIndex.Ssg08] = 10,
        [ItemDefinitionIndex.M249] = 100,
        [ItemDefinitionIndex.Xm1014] = 7,
        [ItemDefinitionIndex.Mag7] = 5,
        [ItemDefinitionIndex.Negev] = 150,
        [ItemDefinitionIndex.Sawedoff] = 7,
        [ItemDefinitionIndex.Nova] = 8
    };

    public MagForMagLastRequest(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public override string Id => "lr_mag_for_mag";
    public override string Name => Core.Localizer["mag_for_mag.name"];
    public override string Description => Core.Localizer["mag_for_mag.description"];
    public override bool AllowAllWeapons => true;
    public override LastRequestOpponentMode OpponentMode => LastRequestOpponentMode.Duel;
    public override int StartCountdown => Main.GlobalConfig.MagForMag.StartCountdown;
    public override bool RequiresWeaponSelection => true;
    public override bool RequiresVariantSelection => false;
    public override LastRequestWeaponSelection WeaponSelection => LastRequestWeaponSelection.Required;
    
    private IJBPlayer? _prisoner;
    private IJBPlayer? _guard;
    private IJBPlayer? _currentShooter;
    private ItemDefinitionIndex? _selectedWeapon;
    private string? _selectedWeaponClassname;
    private int _magazineSize;
    private int _shotsFiredThisTurn;

    private Guid? _weaponFire;
    private readonly Random _random = new();

    public override void Start(LastRequestStartContext context)
    {
        if (context.Guard == null || !context.SelectedWeapon.HasValue)
            return;

        base.Start(context);

        _guard      = context.Guard;
        _prisoner   = context.Prisoner;
        _selectedWeapon = context.SelectedWeapon.Value;
        _selectedWeaponClassname = Core.Helpers.GetClassnameByDefinitionIndex(context.SelectedWeapon.Value);
        _magazineSize = GetMagazineSize(context.SelectedWeapon.Value);
        _shotsFiredThisTurn = 0;

        _weaponFire = Core.GameEvent.HookPost<EventWeaponFire>(OnWeaponFire);

        _currentShooter = _random.Next(0, 2) == 0
            ? _prisoner
            : _guard;

        Core.Scheduler.NextWorldUpdate(() =>
        {
            SetTurnAmmo(_currentShooter);
        });
    }

    public override void End(IJBPlayer? winner, IJBPlayer? loser)
    {
        if (_weaponFire.HasValue)
        {
            Core.GameEvent.Unhook(_weaponFire.Value);
            _weaponFire = null;
        }

        _prisoner = null;
        _guard = null;
        _currentShooter = null;
        _selectedWeapon = null;
        _selectedWeaponClassname = null;
        _magazineSize = 0;
        _shotsFiredThisTurn = 0;

        base.End(winner, loser);
    }

    private HookResult OnWeaponFire(EventWeaponFire e)
    {
        if (e.UserIdPlayer is not IPlayer shooterSender)
            return HookResult.Continue;

        var shooter = Jailbreak.Players.SyncPlayer(shooterSender);
        if (shooter == null || _currentShooter == null || _guard == null || _prisoner == null)
            return HookResult.Continue;

        if (shooter.SteamID != _currentShooter.SteamID || !IsSelectedWeapon(shooter))
            return HookResult.Continue;

        _shotsFiredThisTurn++;
        if (_shotsFiredThisTurn < _magazineSize)
            return HookResult.Continue;

        _currentShooter = _currentShooter.SteamID == _prisoner.SteamID
            ? _guard
            : _prisoner;

        _shotsFiredThisTurn = 0;
        Core.Scheduler.NextWorldUpdate(() =>
        {
            SetTurnAmmo(_currentShooter);
        });

        return HookResult.Continue;
    }

    private void SetTurnAmmo(IJBPlayer? activePlayer)
    {
        if (_prisoner == null || _guard == null || activePlayer == null)
            return;

        SetAmmo(_prisoner, activePlayer.SteamID == _prisoner.SteamID ? _magazineSize : 0);
        SetAmmo(_guard, activePlayer.SteamID == _guard.SteamID ? _magazineSize : 0);
    }

    private void SetAmmo(IJBPlayer player, int ammo)
    {
        var weapon = GetSelectedWeapon(player);
        if (weapon == null || !weapon.IsValid)
            return;

        weapon.Clip1 = ammo;
        weapon.ReserveAmmo[0] = 0;
        weapon.Clip1Updated();
        weapon.ReserveAmmoUpdated();
    }

    private bool IsSelectedWeapon(IJBPlayer player)
    {
        var weapon = player.Player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value;
        return weapon != null
            && weapon.IsValid
            && IsSelectedWeapon(weapon);
    }

    private CBasePlayerWeapon? GetSelectedWeapon(IJBPlayer player)
    {
        var weapons = player.Player.PlayerPawn?.WeaponServices?.MyValidWeapons;
        if (weapons == null)
            return null;

        return weapons.FirstOrDefault(weapon => weapon?.IsValid == true && IsSelectedWeapon(weapon));
    }

    private bool IsSelectedWeapon(CBasePlayerWeapon weapon)
    {
        return !string.IsNullOrEmpty(_selectedWeaponClassname)
            && weapon.DesignerName.Equals(_selectedWeaponClassname, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetMagazineSize(ItemDefinitionIndex weapon)
    {
        return MagazineSizes.GetValueOrDefault(weapon, 1);
    }
}
