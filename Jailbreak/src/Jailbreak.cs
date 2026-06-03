using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Jailbreak.Contract;
using Tomlyn.Extensions.Configuration;

namespace Jailbreak;

[PluginMetadata(
    Name = "Jailbreak",
    Id = "Jailbreak",
    Author = "Marius",
    Version = "1.0.0"
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

        collection.AddSwiftly(Core)
                  .AddSingleton<CuffsManager>()
                  .AddSingleton<IconManager>()
                  .AddSingleton<CellManager>()
                  .AddSingleton<JBPlayerManagement>()
                  .AddSingleton<IJBPlayerManagement, JBPlayerManagement>()
                  .AddSingleton<TeamManager>()
                  .AddSingleton<RebelManager>()
                  .AddSingleton<Api>()
                  .AddSingleton<Events>()
                  .AddSingleton<Listeners>()
                  .AddSingleton<NetMessages>()
                  .AddSingleton<WardenCommands>()
                  .AddSingleton<WardenMenu>()
                  .AddSingleton<BoxManager>();

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

        _provider = collection.BuildServiceProvider();

        if (hotReload)
        {
            _provider.GetRequiredService<JBPlayerManagement>().SyncTeams();
        }

        _provider.GetRequiredService<WardenCommands>().Register();
        _provider.GetRequiredService<Events>().Register();
        _provider.GetRequiredService<Listeners>().Register();
        _provider.GetRequiredService<NetMessages>().Register();
  
        Core.Registrator.Register(_provider.GetRequiredService<RebelManager>());
        Core.Registrator.Register(_provider.GetRequiredService<TeamManager>());
        Core.Registrator.Register(_provider.GetRequiredService<BoxManager>());
        Core.Registrator.Register(_provider.GetRequiredService<CuffsManager>());

    }
    public override void Unload()
    {
        if (_provider == null)
            return;

        _provider.GetRequiredService<WardenCommands>().Unregister();
        _provider.GetRequiredService<Events>().Unregister();
        _provider.GetRequiredService<Listeners>().Unregister();
        _provider.GetRequiredService<NetMessages>().Unregister();
        _provider.GetRequiredService<IconManager>().CleanupAll();
        _provider.Dispose();
        _provider = null;
    }

}
