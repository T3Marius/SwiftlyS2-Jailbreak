using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;

namespace Jailbreak;

public sealed class ShopItemModuleManager
{
    private readonly ILogger<ShopItemModuleManager> _log;
    private readonly Dictionary<string, IItemModule> _modules = new(StringComparer.OrdinalIgnoreCase);

    public ShopItemModuleManager(ILogger<ShopItemModuleManager> log)
    {
        _log = log;
    }

    public IReadOnlyCollection<IItemModule> Modules => _modules.Values.ToArray();

    public bool Register(IItemModule module, IJBShop shop)
    {
        if (string.IsNullOrWhiteSpace(module.Id) || !_modules.TryAdd(module.Id, module))
            return false;

        try
        {
            if (module is IModuleInitializable initializable)
                initializable.Initialize(shop);

            _log.LogInformation("Registered shop item module. Module={ModuleId}", module.Id);
            return true;
        }
        catch (Exception ex)
        {
            _modules.Remove(module.Id);
            _log.LogError(ex, "Failed to initialize shop item module. Module={ModuleId}", module.Id);
            return false;
        }
    }

    public bool Unregister(string moduleId)
    {
        if (!_modules.Remove(moduleId, out var module))
            return false;

        try
        {
            if (module is IModuleInitializable initializable)
                initializable.Shutdown();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to shut down shop item module. Module={ModuleId}", moduleId);
        }

        return true;
    }

    public bool Contains(string moduleId) => _modules.ContainsKey(moduleId);

    public bool CanPurchase(ShopContext context)
    {
        try
        {
            return Resolve(context.Item)?.CanPurchase(context)
                ?? context.Item.CanPurchase(context);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Shop item availability check failed. Item={ItemId}, Module={ModuleId}",
                context.Item.Id, context.Item.ModuleId);
            return false;
        }
    }

    public ShopActionResult Purchase(ShopContext context)
    {
        return Invoke(context, "purchase", module => module.OnPurchase(context), () => context.Item.OnPurchase(context));
    }

    public ShopActionResult Equip(ShopContext context)
    {
        return Invoke(context, "equip", module => module.OnEquip(context), () => context.Item.OnEquip(context));
    }

    public ShopActionResult Unequip(ShopContext context)
    {
        return Invoke(context, "unequip", module => module.OnUnequip(context), () => context.Item.OnUnequip(context));
    }

    public void PrecacheResources(IOnPrecacheResourceEvent e)
    {
        foreach (var module in _modules.Values.ToArray())
        {
            try
            {
                module.OnPrecacheResources(e);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Shop item module resource precache failed. Module={ModuleId}", module.Id);
            }
        }
    }

    public void OnRoundStart()
    {
        foreach (var module in _modules.Values.ToArray())
        {
            try
            {
                module.OnRoundStart();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Shop item module round-start callback failed. Module={ModuleId}", module.Id);
            }
        }
    }

    public void Clear()
    {
        foreach (var moduleId in _modules.Keys.ToArray())
            Unregister(moduleId);
    }

    private IItemModule? Resolve(IShopItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ModuleId))
            return null;

        return _modules.GetValueOrDefault(item.ModuleId);
    }

    private ShopActionResult Invoke(
        ShopContext context,
        string action,
        Func<IItemModule, ShopActionResult> invokeModule,
        Func<ShopActionResult> invokeItem)
    {
        try
        {
            var module = Resolve(context.Item);
            return module != null ? invokeModule(module) : invokeItem();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Shop item {Action} failed. Item={ItemId}, Module={ModuleId}",
                action, context.Item.Id, context.Item.ModuleId);
            return ShopActionResult.Failed(ex.Message);
        }
    }
}
