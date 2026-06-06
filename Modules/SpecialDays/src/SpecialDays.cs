using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Plugins;

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
    public Main(ISwiftlyCore core) : base(core) { }
    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        _jail = interfaceManager.GetSharedInterface<IJailbreak>(IJailbreak.Key);
        if (_jail == null)
        {
            Core.Logger.LogWarning("Jailbreak api is null, special days will not be registered");
            return;
        }

        _jail.RegisterSpecialDay(new KnifeFightDay(Core, _jail));
    }
    public override void Load(bool hotReload)
    {
    }
    public override void Unload()
    {
        _jail?.UnregisterSpecialDay("sd_knife_fight");
    }
}
public sealed class KnifeFightDay : SpecialDayBase
{
    public KnifeFightDay(ISwiftlyCore core, IJailbreak jail)
        : base(core, jail) { }

    public override string Id => "sd_knife_fight";
    public override string Name => "Knife Fight";
    public override string Description => "Everyone fights with knives only.";
    public override int StartCountdown => 10;
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
