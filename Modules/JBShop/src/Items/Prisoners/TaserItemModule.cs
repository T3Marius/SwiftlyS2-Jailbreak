using Jailbreak.Contract;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace JBShop.Items;

public sealed class TaserItemModule : ItemModuleBase, IModuleInitializable
{
    public const string ModuleId = "jbshop.taser";

    public override string Id => ModuleId;

    public void Initialize(IJBShop shop)
    {
        var config = Main.PrisonerItems.Taser;
        if (!config.Enabled)
            return;

        var registered = shop.RegisterItem(new ModuleShopItemDefinition(
            id: config.Id,
            categoryId: Main.GlobalConfig.Prisoners.Id,
            moduleId: Id,
            name: config.Name,
            price: config.Price,
            kind: ShopItemKind.Consumable,
            description: config.Description,
            currency: string.IsNullOrWhiteSpace(config.Currency) ? null : config.Currency));

        if (!registered)
            throw new InvalidOperationException($"Could not register shop item '{config.Id}'.");
    }

    public void Shutdown()
    {
    }

    public override bool CanPurchase(ShopContext context) =>
        context.Player.Player.IsValid && context.Player.Player.IsAlive;

    public override ShopActionResult OnPurchase(ShopContext context)
    {
        var config = Main.PrisonerItems.Taser;

        var pawn = context.Player.Player.Pawn;
        if (pawn == null || !pawn.IsValid)
            return ShopActionResult.Failed("Player is not alive.");

        var taser = pawn.ItemServices?.GiveItem<CBaseEntity>(config.Value);

        return taser?.IsValid == true
            ? ShopActionResult.Succeeded()
            : ShopActionResult.Failed("The taser could not be given.");
    }
}