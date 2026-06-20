using Economy.Contract;
using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;

namespace Jailbreak;

public sealed class ShopManager : IJBShop
{
    public const string EconomyInterfaceKey = "Economy.API.v1";

    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly ShopDatabase _database;
    private readonly ShopItemModuleManager _moduleManager;
    private readonly ILogger<ShopManager> _log;
    private readonly Dictionary<string, ShopCategory> _categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IShopItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, CachedPlayerState> _playerStates = [];
    private readonly Dictionary<ulong, object> _playerLocks = [];
    private readonly object _stateLock = new();

    private IEconomyAPIv1? _economy;
    private Guid? _playerSpawnHookId;

    public ShopManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        ShopDatabase database,
        ShopItemModuleManager moduleManager,
        ILogger<ShopManager> log)
    {
        _core = core;
        _players = players;
        _database = database;
        _moduleManager = moduleManager;
        _log = log;
    }

    public bool IsEconomyAvailable => _economy != null;
    public IReadOnlyCollection<ShopCategory> Categories => _categories.Values.OrderBy(category => category.Order).ToArray();
    public IReadOnlyCollection<IShopItem> Items => _items.Values.ToArray();
    public IReadOnlyCollection<IItemModule> Modules => _moduleManager.Modules;

    public event Action<ShopContext, ShopPurchaseResult>? ItemPurchased;
    public event Action<ShopContext>? ItemEquipped;
    public event Action<ShopContext>? ItemUnequipped;

    public void Register()
    {
        _playerSpawnHookId = _core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        _core.Event.OnClientDisconnected += OnClientDisconnected;
    }

    public void Unregister()
    {
        if (_playerSpawnHookId.HasValue)
        {
            _core.GameEvent.Unhook(_playerSpawnHookId.Value);
            _playerSpawnHookId = null;
        }

        _core.Event.OnClientDisconnected -= OnClientDisconnected;

        foreach (var player in _players.GetAllPlayers())
            UnequipRuntimeItems(player);

        lock (_stateLock)
        {
            _playerStates.Clear();
            _playerLocks.Clear();
        }

        _items.Clear();
        _categories.Clear();
        _moduleManager.Clear();
        _economy = null;
    }

    public void AttachEconomy(IEconomyAPIv1 economy)
    {
        _economy = economy;

        foreach (var currency in _categories.Values.Select(category => category.Currency)
                     .Concat(_items.Values.Select(item => item.Currency))
                     .Where(currency => !string.IsNullOrWhiteSpace(currency))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            EnsureCurrency(currency!);
        }

        _log.LogInformation("Jailbreak shop connected to Economy API.");
    }

    public bool RegisterCategory(ShopCategory category)
    {
        if (string.IsNullOrWhiteSpace(category.Id)
            || string.IsNullOrWhiteSpace(category.Name)
            || string.IsNullOrWhiteSpace(category.Currency))
            return false;

        var normalized = category with
        {
            Id = category.Id.Trim(),
            Name = category.Name.Trim(),
            Currency = category.Currency.Trim()
        };

        if (!_categories.TryAdd(normalized.Id, normalized))
            return false;

        EnsureCurrency(normalized.Currency);
        return true;
    }

    public bool UnregisterCategory(string categoryId)
    {
        if (!_categories.ContainsKey(categoryId))
            return false;

        foreach (var itemId in _items.Values
                     .Where(item => item.CategoryId.Equals(categoryId, StringComparison.OrdinalIgnoreCase))
                     .Select(item => item.Id)
                     .ToArray())
        {
            UnregisterItem(itemId);
        }

        return _categories.Remove(categoryId);
    }

    public bool RegisterItem(IShopItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id)
            || string.IsNullOrWhiteSpace(item.CategoryId)
            || string.IsNullOrWhiteSpace(item.Name)
            || item.Price < 0
            || !_categories.ContainsKey(item.CategoryId))
            return false;

        if (item.Kind == ShopItemKind.Equippable && string.IsNullOrWhiteSpace(item.EquipSlot))
            return false;

        if (!string.IsNullOrWhiteSpace(item.ModuleId) && !_moduleManager.Contains(item.ModuleId))
            return false;

        if (!_items.TryAdd(item.Id, item))
            return false;

        if (!string.IsNullOrWhiteSpace(item.Currency))
            EnsureCurrency(item.Currency!);

        foreach (var player in _players.GetAllPlayers())
        {
            var equipped = GetPlayerState(player.SteamID).EquippedItems.Values;
            if (equipped.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                _core.Scheduler.NextWorldUpdate(() => ReapplyItem(player, item));
        }

        return true;
    }

    public bool UnregisterItem(string itemId)
    {
        if (!_items.TryGetValue(itemId, out var item))
            return false;

        foreach (var player in _players.GetAllPlayers())
        {
            if (!GetPlayerState(player.SteamID).EquippedItems.Values.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                continue;

            if (_categories.TryGetValue(item.CategoryId, out var category))
                SafeUnequipRuntime(player, category, item);
        }

        return _items.Remove(itemId);
    }

    public bool RegisterModule(IItemModule module) =>
        _moduleManager.Register(module, this);

    public bool UnregisterModule(string moduleId)
    {
        foreach (var itemId in _items.Values
                     .Where(item => item.ModuleId?.Equals(moduleId, StringComparison.OrdinalIgnoreCase) == true)
                     .Select(item => item.Id)
                     .ToArray())
        {
            UnregisterItem(itemId);
        }

        return _moduleManager.Unregister(moduleId);
    }

    public ShopCategory? GetCategory(string categoryId) =>
        _categories.GetValueOrDefault(categoryId);

    public IShopItem? GetItem(string itemId) =>
        _items.GetValueOrDefault(itemId);

    public IReadOnlyCollection<IShopItem> GetItems(string categoryId) =>
        _items.Values
            .Where(item => item.CategoryId.Equals(categoryId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public bool CanAccessCategory(IJBPlayer player, string categoryId)
    {
        if (!_categories.TryGetValue(categoryId, out var category))
            return false;

        return category.Scope switch
        {
            ShopCategoryScope.Prisoners => player.Team == JBTeam.Prisoner,
            ShopCategoryScope.Guards => player.Team == JBTeam.Guard,
            _ => player.Team is JBTeam.Prisoner or JBTeam.Guard
        };
    }

    public decimal GetBalance(IJBPlayer player, string currency)
    {
        if (_economy == null || !player.Player.IsValid || string.IsNullOrWhiteSpace(currency))
            return 0;

        EnsureCurrency(currency);
        return _economy.GetPlayerBalance(player.Player, currency);
    }

    public ShopPurchaseResult Purchase(IJBPlayer player, string itemId)
    {
        if (!_items.TryGetValue(itemId, out var item))
            return Result(ShopPurchaseStatus.ItemNotFound, itemId);

        if (!_categories.TryGetValue(item.CategoryId, out var category))
            return Result(ShopPurchaseStatus.CategoryNotFound, itemId);

        var currency = ResolveCurrency(category, item);
        var economy = _economy;
        if (economy == null)
            return Result(ShopPurchaseStatus.EconomyUnavailable, itemId, currency, item.Price);

        if (!player.Player.IsValid || player.Player.IsFakeClient)
            return Result(ShopPurchaseStatus.InvalidPlayer, itemId, currency, item.Price);

        if (!CanAccessCategory(player, category.Id))
            return Result(ShopPurchaseStatus.CategoryRestricted, itemId, currency, item.Price, GetBalance(player, currency));

        var context = new ShopContext(player, category, item, currency);
        var playerLock = GetPlayerLock(player.SteamID);

        lock (playerLock)
        {
            var state = GetPlayerState(player.SteamID);
            var storesOwnership = StoresOwnership(item.Kind);

            if (storesOwnership && state.OwnedItemIds.Contains(item.Id))
                return Result(ShopPurchaseStatus.AlreadyOwned, item.Id, currency, item.Price, GetBalance(player, currency));

            try
            {
                if (!_moduleManager.CanPurchase(context))
                    return Result(ShopPurchaseStatus.ItemUnavailable, item.Id, currency, item.Price, GetBalance(player, currency));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Shop availability check failed. Item={ItemId}, SteamId={SteamId}", item.Id, player.SteamID);
                return Result(ShopPurchaseStatus.ItemUnavailable, item.Id, currency, item.Price, GetBalance(player, currency), ex.Message);
            }

            if (item.Price > 0 && !economy.HasSufficientFunds(player.Player, currency, item.Price))
                return Result(ShopPurchaseStatus.InsufficientFunds, item.Id, currency, item.Price, GetBalance(player, currency));

            if (item.Price > 0)
                economy.SubtractPlayerBalance(player.Player, currency, item.Price);

            ShopActionResult activation;
            try
            {
                activation = _moduleManager.Purchase(context);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Shop item activation threw. Item={ItemId}, SteamId={SteamId}", item.Id, player.SteamID);
                activation = ShopActionResult.Failed(ex.Message);
            }

            if (!activation.Success)
            {
                Refund(player, currency, item.Price);
                return Result(ShopPurchaseStatus.ActivationFailed, item.Id, currency, item.Price, GetBalance(player, currency), activation.Error);
            }

            if (storesOwnership)
            {
                try
                {
                    _database.AddOwnedItem(player.SteamID, item.Id);
                    state.OwnedItemIds.Add(item.Id);
                }
                catch (Exception ex)
                {
                    Refund(player, currency, item.Price);
                    _log.LogError(ex, "Failed to persist shop ownership. Item={ItemId}, SteamId={SteamId}", item.Id, player.SteamID);
                    return Result(ShopPurchaseStatus.PersistenceFailed, item.Id, currency, item.Price, GetBalance(player, currency), ex.Message);
                }
            }

            if (item.Kind == ShopItemKind.Equippable && item.AutoEquipOnPurchase)
                Equip(player, item.Id);

            var result = Result(ShopPurchaseStatus.Success, item.Id, currency, item.Price, GetBalance(player, currency));
            ItemPurchased?.Invoke(context, result);
            return result;
        }
    }

    public bool OwnsItem(IJBPlayer player, string itemId) =>
        GetPlayerState(player.SteamID).OwnedItemIds.Contains(itemId);

    public IReadOnlyCollection<string> GetOwnedItemIds(IJBPlayer player) =>
        GetPlayerState(player.SteamID).OwnedItemIds.ToArray();

    public IReadOnlyDictionary<string, string> GetEquippedItems(IJBPlayer player) =>
        new Dictionary<string, string>(GetPlayerState(player.SteamID).EquippedItems, StringComparer.OrdinalIgnoreCase);

    public ShopActionResult Equip(IJBPlayer player, string itemId)
    {
        if (!_items.TryGetValue(itemId, out var item)
            || item.Kind != ShopItemKind.Equippable
            || string.IsNullOrWhiteSpace(item.EquipSlot)
            || !_categories.TryGetValue(item.CategoryId, out var category))
            return ShopActionResult.Failed("Item cannot be equipped.");

        var playerLock = GetPlayerLock(player.SteamID);
        lock (playerLock)
        {
            var state = GetPlayerState(player.SteamID);
            if (!state.OwnedItemIds.Contains(item.Id))
                return ShopActionResult.Failed("Item is not owned.");

            var context = new ShopContext(player, category, item, ResolveCurrency(category, item));
            state.EquippedItems.TryGetValue(item.EquipSlot!, out var previousItemId);

            if (string.Equals(previousItemId, item.Id, StringComparison.OrdinalIgnoreCase))
                return ShopActionResult.Succeeded();

            IShopItem? previousItem = null;
            ShopContext? previousContext = null;
            if (!string.IsNullOrWhiteSpace(previousItemId)
                && _items.TryGetValue(previousItemId, out previousItem)
                && _categories.TryGetValue(previousItem.CategoryId, out var previousCategory))
            {
                previousContext = new ShopContext(player, previousCategory, previousItem, ResolveCurrency(previousCategory, previousItem));
                _moduleManager.Unequip(previousContext);
            }

            ShopActionResult equipResult;
            try
            {
                equipResult = _moduleManager.Equip(context);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Shop equip threw. Item={ItemId}, SteamId={SteamId}", item.Id, player.SteamID);
                equipResult = ShopActionResult.Failed(ex.Message);
            }

            if (!equipResult.Success)
            {
                if (previousItem != null && previousContext != null)
                    _moduleManager.Equip(previousContext);
                return equipResult;
            }

            try
            {
                _database.SetEquippedItem(player.SteamID, item.EquipSlot!, item.Id);
                state.EquippedItems[item.EquipSlot!] = item.Id;
            }
            catch (Exception ex)
            {
                _moduleManager.Unequip(context);
                if (previousItem != null && previousContext != null)
                    _moduleManager.Equip(previousContext);
                _log.LogError(ex, "Failed to persist equipped shop item. Item={ItemId}, SteamId={SteamId}", item.Id, player.SteamID);
                return ShopActionResult.Failed(ex.Message);
            }

            if (previousContext != null)
                ItemUnequipped?.Invoke(previousContext);
            ItemEquipped?.Invoke(context);
            return ShopActionResult.Succeeded();
        }
    }

    public ShopActionResult Unequip(IJBPlayer player, string itemId)
    {
        if (!_items.TryGetValue(itemId, out var item)
            || item.Kind != ShopItemKind.Equippable
            || string.IsNullOrWhiteSpace(item.EquipSlot)
            || !_categories.TryGetValue(item.CategoryId, out var category))
            return ShopActionResult.Failed("Item cannot be unequipped.");

        var playerLock = GetPlayerLock(player.SteamID);
        lock (playerLock)
        {
            var state = GetPlayerState(player.SteamID);
            if (!state.EquippedItems.TryGetValue(item.EquipSlot!, out var equippedItemId)
                || !equippedItemId.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
                return ShopActionResult.Failed("Item is not equipped.");

            var context = new ShopContext(player, category, item, ResolveCurrency(category, item));
            var result = _moduleManager.Unequip(context);
            if (!result.Success)
                return result;

            _database.RemoveEquippedItem(player.SteamID, item.EquipSlot!);
            state.EquippedItems.Remove(item.EquipSlot!);
            ItemUnequipped?.Invoke(context);
            return ShopActionResult.Succeeded();
        }
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(e.UserIdPlayer);
        if (player == null)
            return HookResult.Continue;

        _core.Scheduler.NextWorldUpdate(() => ReapplyEquippedItems(player));
        return HookResult.Continue;
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent e)
    {
        var rawPlayer = _core.PlayerManager.GetPlayer(e.PlayerId);
        var player = rawPlayer == null ? null : _players.SyncPlayer(rawPlayer);
        if (player != null)
            UnequipRuntimeItems(player);

        var steamId = rawPlayer?.SteamID ?? 0;
        if (steamId == 0)
            return;

        lock (_stateLock)
        {
            _playerStates.Remove(steamId);
            _playerLocks.Remove(steamId);
        }
    }

    private void ReapplyEquippedItems(IJBPlayer player)
    {
        foreach (var itemId in GetPlayerState(player.SteamID).EquippedItems.Values.ToArray())
        {
            if (!_items.TryGetValue(itemId, out var item)
                || !_categories.TryGetValue(item.CategoryId, out var category))
                continue;

            ReapplyItem(player, item, category);
        }
    }

    private void ReapplyItem(IJBPlayer player, IShopItem item, ShopCategory? knownCategory = null)
    {
        var category = knownCategory ?? GetCategory(item.CategoryId);
        if (category == null)
            return;

        try
        {
            _moduleManager.Equip(new ShopContext(player, category, item, ResolveCurrency(category, item)));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to reapply equipped shop item. Item={ItemId}, SteamId={SteamId}", item.Id, player.SteamID);
        }
    }

    private void UnequipRuntimeItems(IJBPlayer player)
    {
        foreach (var itemId in GetPlayerState(player.SteamID).EquippedItems.Values.ToArray())
        {
            if (!_items.TryGetValue(itemId, out var item)
                || !_categories.TryGetValue(item.CategoryId, out var category))
                continue;

            SafeUnequipRuntime(player, category, item);
        }
    }

    private void SafeUnequipRuntime(IJBPlayer player, ShopCategory category, IShopItem item)
    {
        try
        {
            _moduleManager.Unequip(new ShopContext(player, category, item, ResolveCurrency(category, item)));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clean up equipped shop item. Item={ItemId}, SteamId={SteamId}", item.Id, player.SteamID);
        }
    }

    private CachedPlayerState GetPlayerState(ulong steamId)
    {
        lock (_stateLock)
        {
            if (_playerStates.TryGetValue(steamId, out var cached))
                return cached;
        }

        var loaded = _database.LoadPlayerState(steamId);
        var loadedState = new CachedPlayerState(loaded.OwnedItemIds, loaded.EquippedItems);

        lock (_stateLock)
        {
            if (_playerStates.TryGetValue(steamId, out var cached))
                return cached;

            _playerStates[steamId] = loadedState;
            return loadedState;
        }
    }

    private object GetPlayerLock(ulong steamId)
    {
        lock (_stateLock)
        {
            if (!_playerLocks.TryGetValue(steamId, out var playerLock))
            {
                playerLock = new object();
                _playerLocks[steamId] = playerLock;
            }

            return playerLock;
        }
    }

    private void EnsureCurrency(string currency)
    {
        if (_economy == null || string.IsNullOrWhiteSpace(currency))
            return;

        try
        {
            if (!_economy.WalletKindExists(currency))
                _economy.EnsureWalletKind(currency);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to ensure Economy wallet kind {Currency}.", currency);
        }
    }

    private static bool StoresOwnership(ShopItemKind kind) =>
        kind is ShopItemKind.Permanent or ShopItemKind.Equippable;

    private static string ResolveCurrency(ShopCategory category, IShopItem item) =>
        string.IsNullOrWhiteSpace(item.Currency) ? category.Currency : item.Currency!;

    private void Refund(IJBPlayer player, string currency, decimal amount)
    {
        if (amount > 0)
            _economy?.AddPlayerBalance(player.Player, currency, amount);
    }

    private static ShopPurchaseResult Result(
        ShopPurchaseStatus status,
        string itemId,
        string currency = "",
        decimal price = 0,
        decimal balance = 0,
        string? error = null) =>
        new(status, itemId, currency, price, balance, error);

    private sealed record CachedPlayerState(
        HashSet<string> OwnedItemIds,
        Dictionary<string, string> EquippedItems);
}
