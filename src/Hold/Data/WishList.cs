namespace Hold.Data;

public class WishList
{
    /// <summary>
    /// What <see cref="OwnerId"/> held before accounts existed. Nothing writes it any more —
    /// it survives only so the first sign-in can find the rows made when the app had one user
    /// and adopt them. Once claimed, no row carries this value again.
    /// </summary>
    public const string UnclaimedOwnerId = "me";

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

    /// <summary>
    /// The <see cref="User.Id"/> this list belongs to. No default: a list created without an
    /// owner is a bug that should fail loudly rather than quietly become somebody else's.
    /// </summary>
    public required string OwnerId { get; set; }

    /// <summary>
    /// Null unless the list has been shared. When set, anyone holding the token can read the
    /// list at <c>/s/{token}</c> without an account. Clearing it revokes every link already
    /// handed out, which is the only revocation mechanism there is.
    /// </summary>
    public string? ShareToken { get; set; }

    public List<Item> Items { get; set; } = [];
}
