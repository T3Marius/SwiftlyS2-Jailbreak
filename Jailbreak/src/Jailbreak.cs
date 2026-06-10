using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Jailbreak.Contract;
using Tomlyn.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Jailbreak;

[PluginMetadata(
    Name = "Jailbreak",
    Id = "Jailbreak",
    Author = "Marius",
    Version = "0.1.0-beta.9"
)]
public sealed class Main : BasePlugin
{
    private ServiceProvider? _provider;

    public Main(ISwiftlyCore core) : base(core) { }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
        if (_provider == null)
            return;

        var api = _provider.GetRequiredService<Api>();
        interfaceManager.AddSharedInterface<IJailbreak, Api>(IJailbreak.Key, api);
    }
    public override void Load(bool hotReload)
    {
        ServiceCollection collection = new();

        Core.Configuration.InitializeTomlWithModel<WardenConfig>("warden.toml", "Warden")
             .Configure(b => b.AddTomlFile("warden.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<ModelsConfig>("models.toml", "Models")
             .Configure(b => b.AddTomlFile("models.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<UtilsConfig>("utils.toml", "Utils")
             .Configure(b => b.AddTomlFile("utils.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<VoiceConfig>("voice.toml", "Voice")
             .Configure(b => b.AddTomlFile("voice.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<DeputyConfig>("deputy.toml", "Deputy")
             .Configure(b => b.AddTomlFile("deputy.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<SpecialDayConfig>("specialday.toml", "SpecialDay")
             .Configure(b => b.AddTomlFile("specialday.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<PrisonerConfig>("prisoner.toml", "Prisoner")
             .Configure(b => b.AddTomlFile("prisoner.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<JBStatsConfig>("jbstats.toml", "JBStats")
             .Configure(b => b.AddTomlFile("jbstats.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<SoundsConfig>("sounds.toml", "Sounds")
             .Configure(b => b.AddTomlFile("sounds.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<GuardQueueConfig>("queue.toml", "GuardQueue")
             .Configure(b => b.AddTomlFile("queue.toml", false, true));

        collection.AddSwiftly(Core)
                  .AddSingleton<CuffsManager>()
                  .AddSingleton<IconManager>()
                  .AddSingleton<CellManager>()
                  .AddSingleton<JBPlayerManagement>()
                  .AddSingleton<IJBPlayerManagement, JBPlayerManagement>()
                  .AddSingleton<TeamManager>()
                  .AddSingleton<GuardQueueManager>()
                  .AddSingleton<RebelManager>()
                  .AddSingleton<BeaconManager>()
                  .AddSingleton<LaserManager>()
                  .AddSingleton<DrawManager>()
                  .AddSingleton<SpecialDayManager>()
                  .AddSingleton<LastRequestManager>()
                  .AddSingleton<GuardGunsManager>()
                  .AddSingleton<WardenTagManager>()
                  .AddSingleton<JailbreakSoundManager>()
                  .AddSingleton<GameConfig>()
                  .AddSingleton<WardenDatabase>()
                  .AddSingleton<GuardGunsDatabase>()
                  .AddSingleton<JBStatsDB>()
                  .AddSingleton<Api>()
                  .AddSingleton<Events>()
                  .AddSingleton<Listeners>()
                  .AddSingleton<NetMessages>()
                  .AddSingleton<WardenCommands>()
                  .AddSingleton<DeputyCommands>()
                  .AddSingleton<PrisonerCommands>()
                  .AddSingleton<JBStatsCommands>()
                  .AddSingleton<WardenMenu>()
                  .AddSingleton<JBStats>()
                  .AddSingleton<BoxManager>()
                  .AddSingleton<BunnyhoopManager>();

        collection.AddOptionsWithValidateOnStart<WardenConfig>()
                  .BindConfiguration("Warden");

        collection.AddOptionsWithValidateOnStart<ModelsConfig>()
                  .BindConfiguration("Models");

        collection.AddOptionsWithValidateOnStart<UtilsConfig>()
                  .BindConfiguration("Utils");

        collection.AddOptionsWithValidateOnStart<VoiceConfig>()
                  .BindConfiguration("Voice");
        
        collection.AddOptionsWithValidateOnStart<DeputyConfig>()
                  .BindConfiguration("Deputy");

        collection.AddOptionsWithValidateOnStart<SpecialDayConfig>()
                  .BindConfiguration("SpecialDay");

        collection.AddOptionsWithValidateOnStart<PrisonerConfig>()
                  .BindConfiguration("Prisoner");

        collection.AddOptionsWithValidateOnStart<JBStatsConfig>()
                  .BindConfiguration("JBStats");

        collection.AddOptionsWithValidateOnStart<SoundsConfig>()
                  .BindConfiguration("Sounds");

        collection.AddOptionsWithValidateOnStart<GuardQueueConfig>()
                  .BindConfiguration("GuardQueue");

        _provider = collection.BuildServiceProvider();

        _provider.GetRequiredService<WardenDatabase>().Initialize();
        _provider.GetRequiredService<GuardGunsDatabase>().Initialize();
        _provider.GetRequiredService<JBStatsDB>().Initialize();
        _provider.GetRequiredService<GameConfig>().Register(hotReload);

        if (hotReload)
        {
            _provider.GetRequiredService<JBPlayerManagement>().SyncTeams();
        }

        _provider.GetRequiredService<WardenCommands>().Register();
        _provider.GetRequiredService<DeputyCommands>().Register();
        _provider.GetRequiredService<PrisonerCommands>().Register();
        _provider.GetRequiredService<JBStatsCommands>().Register();
        _provider.GetRequiredService<Events>().Register();
        _provider.GetRequiredService<Listeners>().Register();
        _provider.GetRequiredService<NetMessages>().Register();
        _provider.GetRequiredService<SpecialDayManager>().Register();
        _provider.GetRequiredService<LastRequestManager>().Register();
        _provider.GetRequiredService<GuardGunsManager>().Register();
        _provider.GetRequiredService<WardenTagManager>().Register();
        _provider.GetRequiredService<BeaconManager>().Register();
        _provider.GetRequiredService<RebelManager>().Register();
        _provider.GetRequiredService<TeamManager>().Register();
        _provider.GetRequiredService<GuardQueueManager>().Register();
        _provider.GetRequiredService<BoxManager>().Register();
        _provider.GetRequiredService<CuffsManager>().Register();
        _provider.GetRequiredService<LaserManager>().Register();
        _provider.GetRequiredService<DrawManager>().Register();

        if (_provider.GetRequiredService<IOptions<UtilsConfig>>().Value.Bunnyhoop.Enable)
            _provider.GetRequiredService<BunnyhoopManager>().Register();

    }
    public override void Unload()
    {
        if (_provider == null)
            return;

        _provider.GetRequiredService<WardenCommands>().Unregister();
        _provider.GetRequiredService<DeputyCommands>().Unregister();
        _provider.GetRequiredService<PrisonerCommands>().Unregister();
        _provider.GetRequiredService<JBStatsCommands>().Unregister();
        _provider.GetRequiredService<Events>().Unregister();
        _provider.GetRequiredService<Listeners>().Unregister();
        _provider.GetRequiredService<NetMessages>().Unregister();
        _provider.GetRequiredService<SpecialDayManager>().Unregister();
        _provider.GetRequiredService<LastRequestManager>().Unregister();
        _provider.GetRequiredService<GuardGunsManager>().Unregister();
        _provider.GetRequiredService<WardenTagManager>().Unregister();
        _provider.GetRequiredService<GameConfig>().Unregister();
        _provider.GetRequiredService<BeaconManager>().Unregister();
        _provider.GetRequiredService<RebelManager>().Unregister();
        _provider.GetRequiredService<GuardQueueManager>().Unregister();
        _provider.GetRequiredService<TeamManager>().Unregister();
        _provider.GetRequiredService<BoxManager>().Unregister();
        _provider.GetRequiredService<CuffsManager>().Unregister();
        _provider.GetRequiredService<LaserManager>().Unregister();
        _provider.GetRequiredService<DrawManager>().Unregister();
        if (_provider.GetRequiredService<IOptions<UtilsConfig>>().Value.Bunnyhoop.Enable)
            _provider.GetRequiredService<BunnyhoopManager>().Unregister();
        _provider.GetRequiredService<IconManager>().CleanupAll();
        _provider.Dispose();
        _provider = null;
    }

}
