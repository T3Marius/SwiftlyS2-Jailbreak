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
}

public sealed class GlobalItemsConfig
{
}

public sealed class PrisonerItemsConfig
{
    public TaserItemConfig Taser { get; set; } = new();
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
        Description = "Receive a taser for the current life.";
        Price = 250;
    }
}
