namespace Hold.Data;

public class Item
{
    public int Id { get; set; }

    public int WishListId { get; set; }

    public WishList WishList { get; set; } = null!;

    public string? Url { get; set; }

    public required string Title { get; set; }

    public string? Brand { get; set; }

    public string? ImageUrl { get; set; }

    public decimal? Price { get; set; }

    public bool PriceIsEstimate { get; set; }

    public decimal? LatestPrice { get; set; }

    public DateTimeOffset? LatestPriceAt { get; set; }

    public required string Currency { get; set; }

    public Category Category { get; set; }

    public int WaitDays { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public ItemStatus Status { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset ReadyAt => SavedAt.AddDays(WaitDays);

    public bool IsReady(DateTimeOffset now) => Status == ItemStatus.Waiting && now >= ReadyAt;

    public int DaysWaited(DateTimeOffset now) => (int)(now - SavedAt).TotalDays;
}
