using System.Globalization;
using Jailbreak.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Translation;
using T3Menu.Contract;

namespace JBShop;

/// <summary>Uses native submenu history and refreshes item actions without replacing the active menu.</summary>
internal sealed class ShopMenuManager(ISwiftlyCore core, IJBShop shop)
{
    private bool _active = true;
    private readonly Dictionary<int, Menu> _openMenus = [];

    public void Stop()
    {
        _active = false;
        foreach (var menu in _openMenus.Values)
            menu.Close();
        _openMenus.Clear();
    }

    public void OpenMainMenu(IJBPlayer player)
    {
        if (!CanOpen(player)) return;
        var l = Localizer(player);
        var menu = new Menu(l["shop.title"]);
        var categories = shop.Categories.Where(c => shop.CanAccessCategory(player, c.Id)).ToArray();
        if (!shop.IsEconomyAvailable) Info(menu, l["shop.economy_unavailable"]);
        foreach (var category in categories)
        {
            var count = shop.GetItems(category.Id).Count;
            if (count == 0) continue;
            Submenu(menu, player, category.Name, () => BuildCategory(player, category.Id));
        }
        if (!categories.Any(c => shop.GetItems(c.Id).Count > 0)) Info(menu, l["shop.empty"]);
        menu.AddSpacer();
        Submenu(menu, player, l["shop.inventory"], () => BuildInventory(player));
        Show(menu, player);
    }

    private Menu BuildCategory(IJBPlayer player, string categoryId, Menu? menu = null)
    {
        var category = shop.GetCategory(categoryId);
        menu ??= new Menu(category?.Name ?? Localizer(player)["shop.title"]);
        menu.Clear();
        if (category == null || !shop.CanAccessCategory(player, categoryId))
            Info(menu, Localizer(player)["shop.team_restricted"]);
        else
        {
            if (!string.IsNullOrWhiteSpace(category.Description)) Info(menu, category.Description);
            AddItems(menu, player, shop.GetItems(categoryId), () => BuildCategory(player, categoryId, menu));
        }
        return menu;
    }

    private Menu BuildInventory(IJBPlayer player, Menu? menu = null)
    {
        menu ??= new Menu(Localizer(player)["shop.inventory"]);
        menu.Clear();
        var owned = shop.GetOwnedItemIds(player).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddItems(menu, player, shop.Items.Where(i => owned.Contains(i.Id)), () => BuildInventory(player, menu));
        return menu;
    }

    private void AddItems(Menu menu, IJBPlayer player, IEnumerable<IShopItem> items, Action refreshParent)
    {
        var l = Localizer(player);
        var owned = shop.GetOwnedItemIds(player).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var equipped = shop.GetEquippedItems(player).Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sorted = items.OrderByDescending(i => equipped.Contains(i.Id))
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        if (sorted.Length == 0) Info(menu, l["shop.no_items"]);
        foreach (var item in sorted)
        {
            var category = shop.GetCategory(item.CategoryId);
            if (category == null) continue;
            var status = equipped.Contains(item.Id) ? l["shop.status_equipped"]
                : owned.Contains(item.Id) ? l["shop.status_owned"]
                : item.Price == 0 ? l["shop.free"] : $"{Amount(item.Price)} {Currency(category, item)}";
            Submenu(menu, player, l["shop.item", item.Name, status], () => BuildItem(player, item.Id, refreshParent));
        }
    }

    private Menu BuildItem(IJBPlayer player, string itemId, Action refreshParent, Menu? menu = null, string? notice = null)
    {
        var item = shop.GetItem(itemId);
        var category = item == null ? null : shop.GetCategory(item.CategoryId);
        menu ??= new Menu(item?.Name ?? Localizer(player)["shop.title"]);
        menu.Clear();
        if (item == null || category == null)
        {
            Info(menu, Localizer(player)["shop.result_unavailable"]);
            return menu;
        }
        var l = Localizer(player);
        if (notice != null) Info(menu, notice);
        if (!string.IsNullOrWhiteSpace(item.Description)) Info(menu, item.Description);
        var currency = Currency(category, item);
        var owns = shop.OwnsItem(player, item.Id);
        var equipped = shop.GetEquippedItems(player).Values.Contains(item.Id, StringComparer.OrdinalIgnoreCase);
        var accessible = shop.CanAccessCategory(player, category.Id);
        if (!accessible) Info(menu, l["shop.team_restricted"]);
        if (!owns || item.Kind is ShopItemKind.Consumable or ShopItemKind.Temporary)
        {
            Button(menu, player, item.Price == 0 ? l["shop.claim"] : l["shop.buy", Amount(item.Price), currency], () =>
            {
                var result = shop.Purchase(player, item.Id);
                if (result.Status == ShopPurchaseStatus.RoundLimitReached)
                    player.Player.SendChat($"{l["shop.prefix"]}{l["shop.purchase_round_limit", item.Name, item.MaxPurchasesPerRound]}");
                var key = result.Status switch
                {
                    ShopPurchaseStatus.Success => "shop.result_purchased",
                    ShopPurchaseStatus.RoundLimitReached => "shop.result_round_limit",
                    ShopPurchaseStatus.RefundFailed => "shop.result_refund_failed",
                    ShopPurchaseStatus.PaymentFailed => "shop.result_payment_failed",
                    ShopPurchaseStatus.InsufficientFunds => "shop.result_no_funds",
                    ShopPurchaseStatus.AlreadyOwned => "shop.result_owned",
                    ShopPurchaseStatus.CategoryRestricted => "shop.team_restricted",
                    ShopPurchaseStatus.ItemUnavailable => "shop.result_unavailable",
                    ShopPurchaseStatus.EconomyUnavailable => "shop.economy_unavailable",
                    _ => "shop.result_failed"
                };
                refreshParent();
                BuildItem(player, item.Id, refreshParent, menu, l[key]);
            }, !accessible || !shop.IsEconomyAvailable);
        }
        else Info(menu, l[equipped ? "shop.status_equipped" : "shop.status_owned"]);
        if (owns && item.Kind == ShopItemKind.Equippable)
            Button(menu, player, l[equipped ? "shop.unequip" : "shop.equip"], () =>
            {
                // Resolve the operation again: another command may have changed this slot.
                var isEquipped = shop.GetEquippedItems(player).Values.Contains(item.Id, StringComparer.OrdinalIgnoreCase);
                var result = isEquipped ? shop.Unequip(player, item.Id) : shop.Equip(player, item.Id);
                refreshParent();
                BuildItem(player, item.Id, refreshParent, menu, l[result.Success
                    ? isEquipped ? "shop.result_unequipped" : "shop.result_equipped" : "shop.result_failed"]);
            }, !equipped && !accessible);
        menu.Refresh(player.Player);
        return menu;
    }

    private static void Info(Menu menu, string text)
    {
        menu.AddSpacer(text);
        menu.AddSpacer();
    }

    private void Submenu(Menu menu, IJBPlayer player, string label, Func<Menu> build)
    {
        var steamId = player.SteamID;
        menu.AddSubmenu(label, sender =>
        {
            if (!CanOpen(player) || sender.SteamID != steamId) return new Menu(Localizer(player)["shop.title"]);
            var child = build();
            _openMenus[player.Player.PlayerID] = child;
            return child;
        });
    }

    private void Button(Menu menu, IJBPlayer player, string label, Action action, bool disabled = false)
    {
        var steamId = player.SteamID;
        menu.AddItem(label, (sender, _) =>
        {
            if (CanOpen(player) && sender.SteamID == steamId && menu.IsOpen(sender)) action();
        }, disabled);
    }

    private bool CanOpen(IJBPlayer player) => _active && player.Player.IsValid && !player.Player.IsFakeClient && player.SteamID != 0;
    private void Show(Menu menu, IJBPlayer player)
    {
        if (!CanOpen(player)) return;
        _openMenus[player.Player.PlayerID] = menu;
        menu.Open(player.Player);
    }
    private ILocalizer Localizer(IJBPlayer player) => core.Translation.GetPlayerLocalizer(player.Player);
    private static string Currency(ShopCategory category, IShopItem item) => string.IsNullOrWhiteSpace(item.Currency) ? category.Currency : item.Currency!;
    private static string Amount(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);
}
