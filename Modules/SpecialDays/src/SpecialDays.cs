using Jailbreak.Contract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Plugins;
using Tomlyn.Extensions.Configuration;

namespace SpecialDays;

[PluginMetadata(
    Author = "T3Marius",
    Name = "[JB Core] SpecialDays",
    Id = "SpecialDays",
    Version = "1.0.0"
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