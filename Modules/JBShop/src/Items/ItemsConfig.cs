namespace JBShop.Items;

public abstract class ShopItemConfig
{
    public bool Enabled { get; set; } = true;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }

    // Empty uses the currency configured by the item's category.
    public string Currency { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class GlobalItemsConfig
{
}

public sealed class PrisonerItemsConfig
{
    public TaserItemConfig Taser { get; set; } = new();
    public DisguiseItemConfig Disguise { get; set; } = new();
    public BreakCuffsItemConfig BreakCuffs { get; set; } = new();
    public OpenCellsItemConfig OpenCells { get; set; } = new();
}

public sealed class GuardItemsConfig
{
}

public sealed class TaserItemConfig : ShopItemConfig
{
    public TaserItemConfig()
    {
        Id = "jbshop.prisoners.taser";
        Name = "Taser";
        Value = "weapon_taser";
        Description = "Receive a taser that only works once.";
        Price = 500;
    }
}
public sealed class DisguiseItemConfig : ShopItemConfig
{
    public DisguiseItemConfig()
    {
        Id = "jbshop.prisoners.disguise";
        Name = "Disguise";
        Value = "agents/models/sunucukur/guards/g_variant_b/g_variant_b.vmdl";
        Description = "Disguise yourself as a guard. Be careful, they can still identify you!";
        Price = 500;
    }
}

public sealed class BreakCuffsItemConfig : ShopItemConfig
{
    public BreakCuffsItemConfig()
    {
        Id = "jbshop.prisoners.break_cuffs";
        Name = "Break Cuffs";
        Description = "Break free from your cuffs. Can only be purchased once per round.";
        Price = 500;
    }
}

public sealed class OpenCellsItemConfig : ShopItemConfig
{
    public OpenCellsItemConfig()
    {
        Id = "jbshop.prisoners.open_cells";
        Name = "Open Cells";
        Description = "Open all prisoners cells and start a revolt!";
        Price = 1000;
    }
}