namespace JBShop;

public sealed class ShopConfig
{
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

public sealed class ShopCommandsConfig
{
    public List<string> ShopCommands { get; set; } = ["jbshop"];
    public List<string> BalanceCommands { get; set; } = ["balance", "bal"];
    public List<string> GiftCommands { get; set; } = ["gift"];
    public List<string> AddBalanceCommands { get; set; } = ["addbalance"];
    public List<string> SubtractBalanceCommands { get; set; } = ["subtractbalance", "subbalance"];
    public List<string> SetBalanceCommands { get; set; } = ["setbalance"];
    public List<string> AdminPermissions { get; set; } = ["jbshop.admin"];
}

public sealed class ShopCategoryConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "credits";
    public string Description { get; set; } = "";
    public int Order { get; set; }
}
