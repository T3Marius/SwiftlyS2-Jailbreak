namespace JBShop;

public sealed class ShopConfig
{
    public List<string> Commands { get; set; } = ["jbshop"];
    public ShopCategoryConfig Global { get; set; } = new()
    {
        Id = "jbshop.global",
        Name = "Global",
        Currency = "credits",
        Order = 0
    };
    public ShopCategoryConfig Prisoners { get; set; } = new()
    {
        Id = "jbshop.prisoners",
        Name = "Prisoners",
        Currency = "credits",
        Order = 10
    };
    public ShopCategoryConfig Guards { get; set; } = new()
    {
        Id = "jbshop.guards",
        Name = "Guards",
        Currency = "credits",
        Order = 20
    };
}

public sealed class ShopCategoryConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "credits";
    public string Description { get; set; } = "";
    public int Order { get; set; }
}
