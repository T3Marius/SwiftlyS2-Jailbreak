using Jailbreak.Contract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
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
    Version = "1.0.0"
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
        _jail?.UnregisterLastRequest("lr_knife_fight");
    }
}

public sealed class KnifeFightLastRequest : LastRequestBase
{
    private  KnifeFightConfig Config => Main.GlobalConfig.KnifeFight;
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
    public override string Name => "Knife Fight";
    public override string Description => "Fight a guard using knives.";
    public override int StartCountdown => 5;
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
            return pawn.ToPlayer();

        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (player.PlayerPawn?.Address == entity.Address)
                return player;
        }

        return null;
    }
}
