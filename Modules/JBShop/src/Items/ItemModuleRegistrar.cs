using System.Reflection;
using Jailbreak.Contract;
using Microsoft.Extensions.Logging;

namespace JBShop.Items;

public static class ItemModuleRegistrar
{
    public static IReadOnlyCollection<string> RegisterFromAssembly(
        IJBShop shop,
        Assembly assembly,
        ILogger logger)
    {
        var registered = new List<string>();

        foreach (var type in assembly.GetTypes()
                     .Where(type => type is { IsClass: true, IsAbstract: false }
                                    && typeof(IItemModule).IsAssignableFrom(type)))
        {
            try
            {
                if (Activator.CreateInstance(type) is not IItemModule module)
                    continue;

                if (shop.RegisterModule(module))
                    registered.Add(module.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create JBShop item module {ModuleType}.", type.FullName);
            }
        }

        return registered;
    }
}
