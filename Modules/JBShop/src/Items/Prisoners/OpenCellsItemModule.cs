using Jailbreak.Contract;

namespace JBShop.Items;

public sealed class OpenCellsItemModule : ItemModuleBase, IModuleInitializable
{
    public const string ModuleId = "jbshop.open_cells";

    public override string Id => ModuleId;

    public void Initialize(IJBShop shop)
    {
        var config = Main.PrisonerItems.OpenCells;
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

    public void Shutdown() { }

    public override bool CanPurchase(ShopContext context)
    {
        var cells = Main.JailbreakApi?.Cells;
        return cells?.CellsOpen == false;
    }

    public override ShopActionResult OnPurchase(ShopContext context)
    {
        var cells = Main.JailbreakApi?.Cells;
        if (cells?.CellsOpen == true)
            return ShopActionResult.Failed("Cells are already opened!");
        
        cells?.OpenCells();

        return ShopActionResult.Succeeded();
    }
}
