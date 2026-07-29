using Microsoft.EntityFrameworkCore;

namespace Hold.Data;

/// <summary>
/// Development-only seed data, applied at startup rather than through HasData. Seed rows
/// anchored to the current time are non-deterministic, and EF compares them against the
/// model on every build — via HasData they would make the migration regenerate endlessly.
/// Computing the dates here keeps migrations stable and the data meaningful.
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(
        HoldDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        var pending = false;

        if (!await db.Settings.AnyAsync(cancellationToken))
        {
            db.Settings.Add(new Settings { Id = Settings.SingletonId });
            pending = true;
        }

        if (!await db.WishLists.AnyAsync(cancellationToken))
        {
            db.WishLists.AddRange(BuildLists(timeProvider.GetUtcNow()));
            pending = true;
        }

        if (pending)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static IEnumerable<WishList> BuildLists(DateTimeOffset now)
    {
        yield return new WishList
        {
            Name = "Wishlist",
            Description = "The long game.",
            BudgetAmount = 1500.00m,
            BudgetCurrency = "USD",
            CreatedAt = now.AddDays(-40),
            UpdatedAt = now.AddHours(-6),
            Items =
            [
                // Saved 40 days ago against a 30 day wait: already past its date, which is
                // what phase 6 renders in --permission.
                NewItem(
                    "https://shop.doen.com/products/sylvie-coat",
                    "Sylvie Coat",
                    "Dôen",
                    890.00m,
                    Category.Outerwear,
                    waitDays: 30,
                    savedAt: now.AddDays(-40)),
                NewItem(
                    "https://margauxny.com/products/the-classic-ballet-flat",
                    "The Classic Ballet Flat",
                    "Margaux",
                    264.00m,
                    Category.Shoes,
                    waitDays: 30,
                    savedAt: now.AddDays(-12)),
                // Barely started — the other end of the range.
                NewItem(
                    "https://mejuri.com/products/bold-hoops",
                    "Bold Hoops",
                    "Mejuri",
                    50.00m,
                    Category.Jewellery,
                    waitDays: 45,
                    savedAt: now.AddDays(-2)),
            ],
        };

        yield return new WishList
        {
            Name = "Gifts",
            CreatedAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-2),
            Items =
            [
                NewItem(
                    "https://naadam.co/products/the-essential-scarf",
                    "The Essential Scarf",
                    "Naadam",
                    120.00m,
                    Category.Other,
                    waitDays: 14,
                    savedAt: now.AddDays(-18)),
                NewItem(
                    "https://hasamiporcelain.com/products/mug-tall",
                    "Tall Mug, Set of Two",
                    "Hasami Porcelain",
                    85.00m,
                    Category.Home,
                    waitDays: 30,
                    savedAt: now.AddDays(-9)),
                NewItem(
                    "https://hedleyandbennett.com/products/the-essential-apron",
                    "The Essential Apron",
                    "Hedley & Bennett",
                    65.00m,
                    Category.Home,
                    waitDays: 30,
                    savedAt: now.AddDays(-5)),
                NewItem(
                    "https://ferncandles.com/products/beeswax-taper",
                    "Beeswax Taper, Pair",
                    "Fern",
                    40.00m,
                    Category.Home,
                    waitDays: 21,
                    savedAt: now.AddDays(-2)),
            ],
        };

        // Deliberately empty: the Lists card renders five dashed slots for this one.
        yield return new WishList
        {
            Name = "Travel",
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now.AddDays(-11),
        };
    }

    private static Item NewItem(
        string url,
        string title,
        string brand,
        decimal price,
        Category category,
        int waitDays,
        DateTimeOffset savedAt) => new()
        {
            Url = url,
            Title = title,
            Brand = brand,
            Price = price,
            Currency = "USD",
            Category = category,
            WaitDays = waitDays,
            SavedAt = savedAt,
            Status = ItemStatus.Waiting,
        };
}
