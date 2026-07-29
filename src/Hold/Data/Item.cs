namespace Hold.Data;

public class Item
{
    public int Id { get; set; }

    public int WishListId { get; set; }

    public WishList WishList { get; set; } = null!;

    public required string Url { get; set; }

    public required string Title { get; set; }

    public string? Brand { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>
    /// Stored in whatever currency the shop quoted, never converted.
    /// Null when the scraper could not recover a price.
    /// </summary>
    public decimal? Price { get; set; }

    public required string Currency { get; set; }

    public Category Category { get; set; }

    public int WaitDays { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public ItemStatus Status { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public string? Note { get; set; }

    // Ready is computed, never stored. No background job flips a flag, so there are no
    // stale rows. Callers pass `now` in from an injected TimeProvider so this is testable.

    public DateTimeOffset ReadyAt => SavedAt.AddDays(WaitDays);

    public bool IsReady(DateTimeOffset now) => Status == ItemStatus.Waiting && now >= ReadyAt;

    public int DaysWaited(DateTimeOffset now) => (int)(now - SavedAt).TotalDays;
}
