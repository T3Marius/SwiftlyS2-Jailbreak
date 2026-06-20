using Jailbreak.Contract;
using JBShop.Items;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using Tomlyn.Extensions.Configuration;
using System.Reflection;

namespace JBShop;

[PluginMetadata(
    Author = "T3Marius",
    Name = "[JB Core] JBShop",
    Id = "JBShop",
    Version = "0.1.0"
)]
public sealed class Main : BasePlugin
{
    internal static ShopConfig GlobalConfig { get; private set; } = new();
    internal static GlobalItemsConfig GlobalItems { get; private set; } = new();
    internal static PrisonerItemsConfig PrisonerItems { get; private set; } = new();
    internal static GuardItemsConfig GuardItems { get; private set; } = new();

    private ShopConfig _config = new();
    private IJailbreak? _jailbreak;
    private IReadOnlyCollection<string> _registeredModuleIds = [];

    public Main(ISwiftlyCore core) : base(core)
    {
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeTomlWithModel<ShopConfig>("config.toml", "JBShop")
            .Configure(builder => builder.AddTomlFile("config.toml", false, true));
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
        services.AddOptionsWithValidateOnStart<PrisonerItemsConfig>()
            .BindConfiguration("PrisonerItems");
        services.AddOptionsWithValidateOnStart<GuardItemsConfig>()
            .BindConfiguration("GuardItems");

        using var provider = services.BuildServiceProvider();
        GlobalConfig = provider.GetRequiredService<IOptions<ShopConfig>>().Value;
        GlobalItems = provider.GetRequiredService<IOptions<GlobalItemsConfig>>().Value;
        PrisonerItems = provider.GetRequiredService<IOptions<PrisonerItemsConfig>>().Value;
        GuardItems = provider.GetRequiredService<IOptions<GuardItemsConfig>>().Value;
        _config = GlobalConfig;
    }

    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        if (!interfaceManager.TryGetSharedInterface<IJailbreak>(IJailbreak.Key, out var jailbreak))
        {
            Core.Logger.LogWarning("Jailbreak API is unavailable; JBShop will not be registered.");
            return;
        }

        _jailbreak = jailbreak;
        RegisterCategories(_jailbreak.Shop);
        _registeredModuleIds = ItemModuleRegistrar.RegisterFromAssembly(
            _jailbreak.Shop,
            Assembly.GetExecutingAssembly(),
            Core.Logger);

        foreach (var command in _config.Commands)
        {
            if (!Core.Command.IsCommandRegistered(command))
                Core.Command.RegisterCommand(command, OpenShopCommand);
        }
    }

    public override void Unload()
    {
        foreach (var command in _config.Commands)
        {
            if (Core.Command.IsCommandRegistered(command))
                Core.Command.UnregisterCommand(command);
        }

        if (_jailbreak != null)
        {
            foreach (var category in GetConfiguredCategories())
                _jailbreak.Shop.UnregisterCategory(category.Config.Id);

            foreach (var moduleId in _registeredModuleIds)
                _jailbreak.Shop.UnregisterModule(moduleId);
        }
        _registeredModuleIds = [];
        _jailbreak = null;
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

    private void OpenShopCommand(ICommandContext context)
    {
        if (context.Sender == null || _jailbreak == null)
            return;

        var player = _jailbreak.Players.SyncPlayer(context.Sender);
        if (player == null)
            return;

        OpenMainMenu(player);
    }

    private void OpenMainMenu(IJBPlayer player)
    {
        if (_jailbreak == null)
            return;

        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(player.Localizer["shop.title"]);

        var categories = _jailbreak.Shop.Categories
            .Where(category => _jailbreak.Shop.CanAccessCategory(player, category.Id))
            .ToArray();
        if (categories.Length == 0)
        {
            builder.AddOption(new TextMenuOption(player.Localizer["shop.empty"]) { Enabled = false });
        }
        else
        {
            foreach (var category in categories)
            {
                var captured = category;
                var balance = _jailbreak.Shop.GetBalance(player, category.Currency);
                AddButton(builder, player.Localizer["shop.category", category.Name, balance, category.Currency], () =>
                    OpenCategoryMenu(player, captured));
            }
        }

        Core.MenusAPI.OpenMenuForPlayer(player.Player, builder.Build());
    }

    private void OpenCategoryMenu(IJBPlayer player, ShopCategory category)
    {
        if (_jailbreak == null)
            return;

        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(category.Name);

        var items = _jailbreak.Shop.GetItems(category.Id);
        if (items.Count == 0)
        {
            builder.AddOption(new TextMenuOption(player.Localizer["shop.category_empty"]) { Enabled = false });
        }
        else
        {
            var equipped = _jailbreak.Shop.GetEquippedItems(player);
            foreach (var item in items)
            {
                var captured = item;
                var currency = string.IsNullOrWhiteSpace(item.Currency) ? category.Currency : item.Currency!;
                var label = player.Localizer["shop.item", item.Name, item.Price, currency];

                if (equipped.Values.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                    label = player.Localizer["shop.item_equipped", label];
                else if (_jailbreak.Shop.OwnsItem(player, item.Id))
                    label = player.Localizer["shop.item_owned", label];

                AddButton(builder, label, () => OpenItemMenu(player, category, captured));
            }
        }

        Core.MenusAPI.OpenMenuForPlayer(player.Player, builder.Build());
    }

    private void OpenItemMenu(IJBPlayer player, ShopCategory category, IShopItem item)
    {
        if (_jailbreak == null)
            return;

        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(item.Name);

        if (!string.IsNullOrWhiteSpace(item.Description))
            builder.AddOption(new TextMenuOption(player.Localizer["shop.item_description", item.Description]) { Enabled = false });

        var currency = string.IsNullOrWhiteSpace(item.Currency) ? category.Currency : item.Currency!;
        var owns = _jailbreak.Shop.OwnsItem(player, item.Id);
        var equipped = _jailbreak.Shop.GetEquippedItems(player).Values.Contains(item.Id, StringComparer.OrdinalIgnoreCase);

        if (!owns || item.Kind is ShopItemKind.Consumable or ShopItemKind.Temporary)
        {
            AddButton(builder, player.Localizer["shop.buy", item.Price, currency], () => Purchase(player, item));
        }

        if (item.Kind == ShopItemKind.Equippable && owns)
        {
            AddButton(builder, player.Localizer[equipped ? "shop.unequip" : "shop.equip"], () =>
            {
                var result = equipped
                    ? _jailbreak.Shop.Unequip(player, item.Id)
                    : _jailbreak.Shop.Equip(player, item.Id);

                player.SendMessage(
                    MessageType.Chat,
                    result.Success ? (equipped ? "shop.unequip_success" : "shop.equip_success") : "shop.action_failed",
                    true,
                    args: [item.Name]);
                OpenCategoryMenu(player, category);
            });
        }

        Core.MenusAPI.OpenMenuForPlayer(player.Player, builder.Build());
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

        player.SendMessage(MessageType.Chat, message, true, args: args);
    }

    private void AddButton(IMenuBuilderAPI builder, string label, Action action)
    {
        var option = new ButtonMenuOption(label);
        option.Click += (_, _) =>
        {
            Core.Scheduler.NextWorldUpdate(action);
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }
}
