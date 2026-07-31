namespace Hold.Data;

public class WishList
{
    public const string UnclaimedOwnerId = "me";

    public const string DemoOwnerId = "demo";

    public const string DemoShareToken = "demo";

    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal? BudgetAmount { get; set; }

    public string? BudgetCurrency { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public required string OwnerId { get; set; }

    public string? ShareToken { get; set; }

    public List<Item> Items { get; set; } = [];
}
