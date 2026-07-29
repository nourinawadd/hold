namespace Hold.Data;

public class WishList
{
    /// <summary>
    /// There is no auth in v1. Every list is owned by this constant so that adding
    /// accounts later is a change of one expression rather than a rewrite of every query.
    /// </summary>
    public const string DefaultOwnerId = "me";

    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// The budget bar counts only items whose currency matches <see cref="BudgetCurrency"/>.
    /// Amounts are never converted between currencies.
    /// </summary>
    public decimal? BudgetAmount { get; set; }

    public string? BudgetCurrency { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string OwnerId { get; set; } = DefaultOwnerId;

    public List<Item> Items { get; set; } = [];
}
