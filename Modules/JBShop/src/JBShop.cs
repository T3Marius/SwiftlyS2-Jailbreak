using Jailbreak.Contract;
using JBShop.Items;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using Tomlyn.Extensions.Configuration;
using System.Reflection;
using T3Menu.Contract;

namespace JBShop;

[PluginMetadata(
    Author = "T3Marius",
    Name = "[JB Core] JBShop",
    Id = "JBShop",
    Version = "0.1.6"
)]
public sealed class Main : BasePlugin
{
    internal static ShopConfig GlobalConfig { get; private set; } = new();
    internal static ShopCommandsConfig CommandsConfig { get; private set; } = new();
    internal static GlobalItemsConfig GlobalItems { get; private set; } = new();
    internal static PrisonerItemsConfig PrisonerItems { get; private set; } = new();
    internal static GuardItemsConfig GuardItems { get; private set; } = new();
    internal static IJailbreak? JailbreakApi { get; private set; }

    private ShopConfig _config = new();
    private IJailbreak? _jailbreak;
    private ShopCommandManager? _commandManager;
    private ShopMenuManager? _menuManager;
    private readonly HashSet<string> _registeredCategoryIds = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyCollection<string> _registeredModuleIds = [];

    public Main(ISwiftlyCore core) : base(core)
    {
    }
    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeTomlWithModel<ShopConfig>("config.toml", "JBShop")
            .Configure(builder => builder.AddTomlFile("config.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<ShopCommandsConfig>("commands.toml", "Commands")
            .Configure(builder => builder.AddTomlFile("commands.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<GlobalItemsConfig>("global_items.toml", "GlobalItems")
            .Configure(builder => builder.AddTomlFile("global_items.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<PrisonerItemsConfig>("prisoners_items.toml", "PrisonerItems")
            .Configure(builder => builder.AddTomlFile("prisoners_items.toml", false, true));
        Core.Configuration.InitializeTomlWithModel<GuardItemsConfig>("guards_items.toml", "GuardItems")
            .Configure(builder => builder.AddTomlFile("guards_items.toml", false, true));

        ServiceCollection services = new();
        services.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<ShopConfig>()
            .BindConfiguration("JBShop");
        services.AddOptionsWithValidateOnStart<GlobalItemsConfig>()
            .BindConfiguration("GlobalItems");
        services.AddOptionsWithValidateOnStart<ShopCommandsConfig>()
            .BindConfiguration("Commands");
        services.AddOptionsWithValidateOnStart<PrisonerItemsConfig>()
            .BindConfiguration("PrisonerItems");
        services.AddOptionsWithValidateOnStart<GuardItemsConfig>()
            .BindConfiguration("GuardItems");

        using var provider = services.BuildServiceProvider();
        GlobalConfig = provider.GetRequiredService<IOptions<ShopConfig>>().Value;
        CommandsConfig = provider.GetRequiredService<IOptions<ShopCommandsConfig>>().Value;
        GlobalItems = provider.GetRequiredService<IOptions<GlobalItemsConfig>>().Value;
        PrisonerItems = provider.GetRequiredService<IOptions<PrisonerItemsConfig>>().Value;
        GuardItems = provider.GetRequiredService<IOptions<GuardItemsConfig>>().Value;
        _config = GlobalConfig;
        _commandManager = new ShopCommandManager(Core, CommandsConfig);
    }

    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        if (!interfaceManager.TryGetSharedInterface<IJailbreak>(IJailbreak.Key, out var jailbreak))
        {
            Core.Logger.LogWarning("Jailbreak API is unavailable; JBShop will not be registered.");
            return;
        }

        if (_jailbreak != null) return;
        IT3Menu.Inject(interfaceManager);
        _jailbreak = jailbreak;
        JailbreakApi = jailbreak;
        RegisterCategories(_jailbreak.Shop);
        _registeredModuleIds = ItemModuleRegistrar.RegisterFromAssembly(
            _jailbreak.Shop,
            Assembly.GetExecutingAssembly(),
            Core.Logger);

        _menuManager = new ShopMenuManager(Core, jailbreak.Shop);
        _commandManager?.Register(jailbreak, _menuManager.OpenMainMenu);
    }

    public override void Unload()
    {
        _commandManager?.Unregister();
        _menuManager?.Stop();
        _menuManager = null;

        if (_jailbreak != null)
        {
            foreach (var categoryId in _registeredCategoryIds)
                _jailbreak.Shop.UnregisterCategory(categoryId);

            foreach (var moduleId in _registeredModuleIds)
                _jailbreak.Shop.UnregisterModule(moduleId);
        }
        _registeredCategoryIds.Clear();
        _registeredModuleIds = [];
        _jailbreak = null;
        JailbreakApi = null;
    }

    private void RegisterCategories(IJBShop shop)
    {
        foreach (var category in GetConfiguredCategories())
        {
            if (shop.RegisterCategory(new ShopCategory(
                category.Config.Id,
                category.Config.Name,
                category.Config.Currency,
                category.Scope,
                category.Config.Description,
                category.Config.Order)))
                _registeredCategoryIds.Add(category.Config.Id.Trim());
            else
                Core.Logger.LogWarning("Could not register shop category {CategoryId}; check duplicate IDs and configuration.", category.Config.Id);
        }
    }

    private IEnumerable<(ShopCategoryConfig Config, ShopCategoryScope Scope)> GetConfiguredCategories()
    {
        yield return (_config.Global, ShopCategoryScope.Global);
        yield return (_config.Prisoners, ShopCategoryScope.Prisoners);
        yield return (_config.Guards, ShopCategoryScope.Guards);
    }

}
