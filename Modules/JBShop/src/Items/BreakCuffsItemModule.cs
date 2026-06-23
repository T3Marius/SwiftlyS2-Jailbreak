using Jailbreak.Contract;

namespace JBShop.Items;

public sealed class BreakCuffsItemModule : ItemModuleBase, IModuleInitializable
{
    public const string ModuleId = "jbshop.break_cuffs";

    private readonly HashSet<ulong> _purchasedThisRound = [];

    public override string Id => ModuleId;

    public void Initialize(IJBShop shop)
    {
        var config = Main.PrisonerItems.BreakCuffs;
        if (!config.Enabled)
            return;

        if (!shop.RegisterItem(new ModuleShopItemDefinition(
                id: config.Id,
                categoryId: Main.GlobalConfig.Prisoners.Id,
                moduleId: Id,
                name: config.Name,
                price: config.Price,
                kind: ShopItemKind.Consumable,
                description: config.Description,
                currency: string.IsNullOrWhiteSpace(config.Currency) ? null : config.Currency)))
        {
            throw new InvalidOperationException($"Could not register shop item '{config.Id}'.");
        }
    }

    public void Shutdown() =>
        _purchasedThisRound.Clear();

    public override void OnRoundStart() =>
        _purchasedThisRound.Clear();

    public override bool CanPurchase(ShopContext context)
    {
        var cuffs = Main.JailbreakApi?.Cuffs;
        return cuffs != null
            && context.Player.Team == JBTeam.Prisoner
            && !_purchasedThisRound.Contains(context.Player.SteamID)
            && cuffs.IsCuffed(context.Player);
    }

    public override ShopActionResult OnPurchase(ShopContext context)
    {
        var cuffs = Main.JailbreakApi?.Cuffs;
        if (cuffs == null || !cuffs.TryBreakCuffs(context.Player))
            return ShopActionResult.Failed("You are not cuffed.");

        _purchasedThisRound.Add(context.Player.SteamID);
        return ShopActionResult.Succeeded();
    }
}
