using System.Collections.ObjectModel;
using SwiftlyS2.Shared.Events;

namespace Jailbreak.Contract;

public enum ShopItemKind
{
    Consumable = 0,
    Temporary = 1,
    Permanent = 2,
    Equippable = 3
}

public enum ShopCategoryScope
{
    Global = 0,
    Prisoners = 1,
    Guards = 2
}

public enum ShopPurchaseStatus
{
    Success = 0,
    EconomyUnavailable = 1,
    InvalidPlayer = 2,
    ItemNotFound = 3,
    CategoryNotFound = 4,
    CategoryRestricted = 5,
    ItemUnavailable = 6,
    AlreadyOwned = 7,
    InsufficientFunds = 8,
    ActivationFailed = 9,
    PersistenceFailed = 10
}

public enum ShopBalanceStatus
{
    Success = 0,
    EconomyUnavailable = 1,
    InvalidPlayer = 2,
    InvalidCurrency = 3,
    InvalidAmount = 4,
    InsufficientFunds = 5,
    SamePlayer = 6,
    Failed = 7
}

public readonly record struct ShopActionResult(bool Success, string? Error = null)
{
    public static ShopActionResult Succeeded() => new(true);
    public static ShopActionResult Failed(string? error = null) => new(false, error);
}

public sealed record ShopPurchaseResult(
    ShopPurchaseStatus Status,
    string ItemId,
    string Currency,
    decimal Price,
    decimal Balance,
    string? Error = null)
{
    public bool Success => Status == ShopPurchaseStatus.Success;
}

public readonly record struct ShopBalanceResult(ShopBalanceStatus Status, decimal Balance = 0)
{
    public bool Success => Status == ShopBalanceStatus.Success;
}

public sealed record ShopCategory(
    string Id,
    string Name,
    string Currency,
    ShopCategoryScope Scope = ShopCategoryScope.Global,
    string Description = "",
    int Order = 0);

public sealed record ShopContext(
    IJBPlayer Player,
    ShopCategory Category,
    IShopItem Item,
    string Currency);

public interface IShopItem
{
    string Id { get; }
    string CategoryId { get; }
    string Name { get; }
    string Description { get; }
    decimal Price { get; }

    /// <summary>Null or empty uses the category currency.</summary>
    string? Currency { get; }

    ShopItemKind Kind { get; }

    /// <summary>Required for equippable items. Items sharing a slot replace each other.</summary>
    string? EquipSlot { get; }

    bool AutoEquipOnPurchase { get; }

    /// <summary>Null uses the behavior implemented by this item instance.</summary>
    string? ModuleId { get; }

    /// <summary>Module-specific immutable item settings.</summary>
    IReadOnlyDictionary<string, string> Data { get; }

    bool CanPurchase(ShopContext context);
    ShopActionResult OnPurchase(ShopContext context);
    ShopActionResult OnEquip(ShopContext context);
    ShopActionResult OnUnequip(ShopContext context);
}

public abstract class ShopItemBase : IShopItem
{
    private static readonly IReadOnlyDictionary<string, string> EmptyData =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public abstract string Id { get; }
    public abstract string CategoryId { get; }
    public abstract string Name { get; }
    public virtual string Description => "";
    public abstract decimal Price { get; }
    public virtual string? Currency => null;
    public virtual ShopItemKind Kind => ShopItemKind.Consumable;
    public virtual string? EquipSlot => null;
    public virtual bool AutoEquipOnPurchase => Kind == ShopItemKind.Equippable;
    public virtual string? ModuleId => null;
    public virtual IReadOnlyDictionary<string, string> Data => EmptyData;

    public virtual bool CanPurchase(ShopContext context) => true;
    public abstract ShopActionResult OnPurchase(ShopContext context);
    public virtual ShopActionResult OnEquip(ShopContext context) => ShopActionResult.Succeeded();
    public virtual ShopActionResult OnUnequip(ShopContext context) => ShopActionResult.Succeeded();
}

public sealed class ShopItemDefinition : ShopItemBase
{
    private readonly Func<ShopContext, bool>? _canPurchase;
    private readonly Func<ShopContext, ShopActionResult> _onPurchase;
    private readonly Func<ShopContext, ShopActionResult>? _onEquip;
    private readonly Func<ShopContext, ShopActionResult>? _onUnequip;

    public ShopItemDefinition(
        string id,
        string categoryId,
        string name,
        decimal price,
        ShopItemKind kind,
        Func<ShopContext, ShopActionResult> onPurchase,
        string description = "",
        string? currency = null,
        string? equipSlot = null,
        bool? autoEquipOnPurchase = null,
        Func<ShopContext, bool>? canPurchase = null,
        Func<ShopContext, ShopActionResult>? onEquip = null,
        Func<ShopContext, ShopActionResult>? onUnequip = null)
    {
        Id = id;
        CategoryId = categoryId;
        Name = name;
        Price = price;
        Kind = kind;
        Description = description;
        Currency = currency;
        EquipSlot = equipSlot;
        AutoEquipOnPurchase = autoEquipOnPurchase ?? kind == ShopItemKind.Equippable;
        _canPurchase = canPurchase;
        _onPurchase = onPurchase;
        _onEquip = onEquip;
        _onUnequip = onUnequip;
    }

    public override string Id { get; }
    public override string CategoryId { get; }
    public override string Name { get; }
    public override string Description { get; }
    public override decimal Price { get; }
    public override string? Currency { get; }
    public override ShopItemKind Kind { get; }
    public override string? EquipSlot { get; }
    public override bool AutoEquipOnPurchase { get; }

    public override bool CanPurchase(ShopContext context) =>
        _canPurchase?.Invoke(context) ?? true;

    public override ShopActionResult OnPurchase(ShopContext context) =>
        _onPurchase(context);

    public override ShopActionResult OnEquip(ShopContext context) =>
        _onEquip?.Invoke(context) ?? ShopActionResult.Succeeded();

    public override ShopActionResult OnUnequip(ShopContext context) =>
        _onUnequip?.Invoke(context) ?? ShopActionResult.Succeeded();
}

public sealed class ModuleShopItemDefinition : ShopItemBase
{
    public ModuleShopItemDefinition(
        string id,
        string categoryId,
        string moduleId,
        string name,
        decimal price,
        ShopItemKind kind,
        string description = "",
        string? currency = null,
        string? equipSlot = null,
        bool? autoEquipOnPurchase = null,
        IReadOnlyDictionary<string, string>? data = null)
    {
        Id = id;
        CategoryId = categoryId;
        ModuleId = moduleId;
        Name = name;
        Price = price;
        Kind = kind;
        Description = description;
        Currency = currency;
        EquipSlot = equipSlot;
        AutoEquipOnPurchase = autoEquipOnPurchase ?? kind == ShopItemKind.Equippable;
        Data = new ReadOnlyDictionary<string, string>(
            data == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase));
    }

    public override string Id { get; }
    public override string CategoryId { get; }
    public override string ModuleId { get; }
    public override string Name { get; }
    public override string Description { get; }
    public override decimal Price { get; }
    public override string? Currency { get; }
    public override ShopItemKind Kind { get; }
    public override string? EquipSlot { get; }
    public override bool AutoEquipOnPurchase { get; }
    public override IReadOnlyDictionary<string, string> Data { get; }

    public override ShopActionResult OnPurchase(ShopContext context) =>
        ShopActionResult.Failed($"Item module '{ModuleId}' is not registered.");
}

public interface IItemModule
{
    string Id { get; }

    bool CanPurchase(ShopContext context);
    ShopActionResult OnPurchase(ShopContext context);
    ShopActionResult OnEquip(ShopContext context);
    ShopActionResult OnUnequip(ShopContext context);
    void OnPrecacheResources(IOnPrecacheResourceEvent e)
    {
    }
}

public interface IModuleInitializable
{
    void Initialize(IJBShop shop);
    void Shutdown()
    {
    }
}

public abstract class ItemModuleBase : IItemModule
{
    public abstract string Id { get; }
    public virtual bool CanPurchase(ShopContext context) => true;
    public abstract ShopActionResult OnPurchase(ShopContext context);
    public virtual ShopActionResult OnEquip(ShopContext context) => ShopActionResult.Succeeded();
    public virtual ShopActionResult OnUnequip(ShopContext context) => ShopActionResult.Succeeded();
    public virtual void OnPrecacheResources(IOnPrecacheResourceEvent e)
    {
    }
}

public interface IJBShop
{
    bool IsEconomyAvailable { get; }
    IReadOnlyCollection<ShopCategory> Categories { get; }
    IReadOnlyCollection<IShopItem> Items { get; }
    IReadOnlyCollection<IItemModule> Modules { get; }
    IReadOnlyCollection<string> Currencies { get; }

    event Action<ShopContext, ShopPurchaseResult>? ItemPurchased;
    event Action<ShopContext>? ItemEquipped;
    event Action<ShopContext>? ItemUnequipped;

    bool RegisterCategory(ShopCategory category);
    bool UnregisterCategory(string categoryId);
    bool RegisterItem(IShopItem item);
    bool UnregisterItem(string itemId);
    bool RegisterModule(IItemModule module);
    bool UnregisterModule(string moduleId);

    ShopCategory? GetCategory(string categoryId);
    IShopItem? GetItem(string itemId);
    IReadOnlyCollection<IShopItem> GetItems(string categoryId);
    bool CanAccessCategory(IJBPlayer player, string categoryId);

    decimal GetBalance(IJBPlayer player, string currency);
    ShopBalanceResult AddBalance(IJBPlayer player, string currency, decimal amount);
    ShopBalanceResult SubtractBalance(IJBPlayer player, string currency, decimal amount);
    ShopBalanceResult SetBalance(IJBPlayer player, string currency, decimal amount);
    ShopBalanceResult TransferBalance(IJBPlayer sender, IJBPlayer recipient, string currency, decimal amount);
    ShopPurchaseResult Purchase(IJBPlayer player, string itemId);

    bool OwnsItem(IJBPlayer player, string itemId);
    IReadOnlyCollection<string> GetOwnedItemIds(IJBPlayer player);
    IReadOnlyDictionary<string, string> GetEquippedItems(IJBPlayer player);
    ShopActionResult Equip(IJBPlayer player, string itemId);
    ShopActionResult Unequip(IJBPlayer player, string itemId);
}
