using Jailbreak.Contract;
using JBShop.Items;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Translation;
using Tomlyn.Extensions.Configuration;
using System.Reflection;
using T3Menu.Contract;
using SwiftlyS2.Shared.Players;

namespace JBShop;

[PluginMetadata(
    Author = "T3Marius",
    Name = "[JB Core] JBShop",
    Id = "JBShop",
    Version = "0.1.5"
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

        _jailbreak = jailbreak;
        JailbreakApi = jailbreak;
        RegisterCategories(_jailbreak.Shop);
        _registeredModuleIds = ItemModuleRegistrar.RegisterFromAssembly(
            _jailbreak.Shop,
            Assembly.GetExecutingAssembly(),
            Core.Logger);

        _commandManager?.Register(jailbreak, OpenMainMenu);

        IT3Menu.Inject(interfaceManager);
    }

    public override void Unload()
    {
        _commandManager?.Unregister();

        if (_jailbreak != null)
        {
            foreach (var category in GetConfiguredCategories())
                _jailbreak.Shop.UnregisterCategory(category.Config.Id);

            foreach (var moduleId in _registeredModuleIds)
                _jailbreak.Shop.UnregisterModule(moduleId);
        }
        _registeredModuleIds = [];
        _jailbreak = null;
        JailbreakApi = null;
    }

    private void RegisterCategories(IJBShop shop)
    {
        foreach (var category in GetConfiguredCategories())
        {
            shop.RegisterCategory(new ShopCategory(
                category.Config.Id,
                category.Config.Name,
                category.Config.Currency,
                category.Scope,
                category.Config.Description,
                category.Config.Order));
        }
    }

    private IEnumerable<(ShopCategoryConfig Config, ShopCategoryScope Scope)> GetConfiguredCategories()
    {
        yield return (_config.Global, ShopCategoryScope.Global);
        yield return (_config.Prisoners, ShopCategoryScope.Prisoners);
        yield return (_config.Guards, ShopCategoryScope.Guards);
    }

    private void OpenMainMenu(IJBPlayer player)
    {
        if (_jailbreak == null)
            return;

        var localizer = GetLocalizer(player);

        Menu menu = new Menu(localizer["shop.title"]);

        var categories = _jailbreak.Shop.Categories
            .Where(category => _jailbreak.Shop.CanAccessCategory(player, category.Id))
            .ToArray();
        if (categories.Length == 0)
        {
            menu.AddSpacer(localizer["shop.empty"]);
        }
        else
        {
            foreach (var category in categories)
            {
                var captured = category;
                var balance = _jailbreak.Shop.GetBalance(player, category.Currency);
                menu.AddSubmenu(localizer["shop.category", category.Name, balance, category.Currency],
                    () => BuildCategoryMenu(player, captured));
            }
        }

        menu.Open(player.Player);
    }

    private Menu BuildCategoryMenu(IJBPlayer player, ShopCategory category)
    {
        if (_jailbreak == null)
            throw new InvalidOperationException("Jailbreak API is unavailable.");

        var localizer = GetLocalizer(player);

        Menu menu = new Menu(category.Name);

        var items = _jailbreak.Shop.GetItems(category.Id);
        if (items.Count == 0)
        {
            menu.AddSpacer(localizer["shop.category_empty"]);
        }
        else
        {
            var equipped = _jailbreak.Shop.GetEquippedItems(player);
            foreach (var item in items)
            {
                var captured = item;
                var currency = string.IsNullOrWhiteSpace(item.Currency) ? category.Currency : item.Currency!;
                var label = localizer["shop.item", item.Name, item.Price, currency];

                if (equipped.Values.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                    label = localizer["shop.item_equipped", label];
                else if (_jailbreak.Shop.OwnsItem(player, item.Id))
                    label = localizer["shop.item_owned", label];

                menu.AddSubmenu(label, () => BuildItemMenu(player, category, captured));
            }
        }

        return menu;
    }

    private Menu BuildItemMenu(IJBPlayer player, ShopCategory category, IShopItem item)
    {
        if (_jailbreak == null)
            throw new InvalidOperationException("Jailbreak API is unavailable.");

        var localizer = GetLocalizer(player);
        var menu = new Menu(item.Name);

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            menu.AddSpacer(localizer["shop.item_description", item.Description]);
        }

        var currency = string.IsNullOrWhiteSpace(item.Currency) ? category.Currency : item.Currency!;
        var owns = _jailbreak.Shop.OwnsItem(player, item.Id);
        var equipped = _jailbreak.Shop.GetEquippedItems(player).Values.Contains(item.Id, StringComparer.OrdinalIgnoreCase);

        if (!owns || item.Kind is ShopItemKind.Consumable or ShopItemKind.Temporary)
        {
            AddButton(menu, localizer["shop.buy", item.Price, currency], (p, i) => Purchase(player, item));
        }

        if (item.Kind == ShopItemKind.Equippable && owns)
        {
            AddButton(menu, localizer[equipped ? "shop.unequip" : "shop.equip"], (p, i) =>
            {
                var result = equipped
                    ? _jailbreak.Shop.Unequip(player, item.Id)
                    : _jailbreak.Shop.Equip(player, item.Id);

                SendMessage(
                    player,
                    result.Success ? (equipped ? "shop.unequip_success" : "shop.equip_success") : "shop.action_failed",
                    item.Name);
            });
        }

        return menu;
    }

    private void Purchase(IJBPlayer player, IShopItem item)
    {
        if (_jailbreak == null)
            return;

        var result = _jailbreak.Shop.Purchase(player, item.Id);
        var message = result.Status switch
        {
            ShopPurchaseStatus.Success => "shop.purchase_success",
            ShopPurchaseStatus.InsufficientFunds => "shop.purchase_no_funds",
            ShopPurchaseStatus.AlreadyOwned => "shop.purchase_owned",
            ShopPurchaseStatus.CategoryRestricted => "shop.purchase_restricted",
            ShopPurchaseStatus.ItemUnavailable => "shop.purchase_unavailable",
            ShopPurchaseStatus.EconomyUnavailable => "shop.economy_unavailable",
            _ => "shop.purchase_failed"
        };

        object[] args = result.Status switch
        {
            ShopPurchaseStatus.Success => [item.Name, result.Price, result.Currency],
            ShopPurchaseStatus.InsufficientFunds => [result.Currency],
            ShopPurchaseStatus.AlreadyOwned or ShopPurchaseStatus.ItemUnavailable => [item.Name],
            _ => [item.Name]
        };

        SendMessage(player, message, args);
        Core.MenusAPI.CloseActiveMenu(player.Player);
    }

    private ILocalizer GetLocalizer(IJBPlayer player) =>
        Core.Translation.GetPlayerLocalizer(player.Player);

    private void SendMessage(IJBPlayer player, string key, params object[] args)
    {
        var localizer = GetLocalizer(player);
        var message = args.Length == 0 ? localizer[key] : localizer[key, args];
        player.Player.SendChat($"{localizer["shop.prefix"]}{message}");
    }

    private void AddButton(Menu builder, string label, Action<IPlayer, MenuItem> action)
    {
        builder.AddItem(label, action);
    }
}
