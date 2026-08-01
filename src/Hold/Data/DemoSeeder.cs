using Microsoft.EntityFrameworkCore;

namespace Hold.Data;

public static class DemoSeeder
{
    public static async Task SeedAsync(
        HoldDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        await db.WishLists
            .Where(list => list.ShareToken == WishList.DemoShareToken)
            .ExecuteDeleteAsync(cancellationToken);

        db.WishLists.Add(Build(timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static WishList Build(DateTimeOffset now) => new()
    {
        OwnerId = WishList.DemoOwnerId,
        ShareToken = WishList.DemoShareToken,
        Name = "Wishlist",
        Description = "An example of what Hold looks like in use.",
        CreatedAt = now.AddDays(-96),
        UpdatedAt = now.AddDays(-2),
        Items =
        [
            Item(
                "https://heliostreetwear.com/collections/leather-line/products/miss-mess-bag-brown",
                "MISS MESS BAG BROWN",
                "Helio",
                850.00m,
                Category.Bags,
                "https://cdn.shopify.com/s/files/1/0670/1649/1239/files/IMG_6703.jpg?v=1778610712&width=480",
                waitDays: 30,
                savedAt: now.AddDays(-96)),
            Item(
                "https://worood.co/collections/printed/products/umbrea",
                "Umbréa Printed Premium Modal Scarf",
                "Worood",
                1490.00m,
                Category.Accessories,
                "https://cdn.shopify.com/s/files/1/0703/7173/7873/files/Umbrea_a_Modal_Scarf_stylish_and_mysterious_by_worood.jpg?v=1779713735&width=480",
                waitDays: 45,
                savedAt: now.AddDays(-61)),
            Item(
                "https://www.ikea.com/eg/en/p/fejka-artificial-potted-plant-in-outdoor-eucalyptus-20615272/",
                "FEJKA Artificial potted plant - in/outdoor eucalyptus 15 cm",
                "IKEA",
                1299.00m,
                Category.Home,
                "https://www.ikea.com/eg/en/images/p/3595ad6a6a3e1f39/fejka-artificial-potted-plant-in-outdoor-eucalyptus/PE1007514.jpg?f=xl",
                waitDays: 60,
                savedAt: now.AddDays(-34)),
            Item(
                "https://www.ikea.com/eg/en/p/stockholm-2025-mug-brown-00592417/",
                "STOCKHOLM 2025 Mug - brown 21 cl",
                "IKEA",
                489.00m,
                Category.Kitchen,
                "https://www.ikea.com/eg/en/images/products/stockholm-2025-mug-brown__1424592_ph203179_s5.jpg?f=xl",
                waitDays: 30,
                savedAt: now.AddDays(-21)),
            Item(
                "https://www.pullandbear.com/eg/en/leather-effect-balloon-jacket-l07720317?cS=717&pelement=748862734",
                "Leather Effect Balloon Jacket",
                "Pullandbear",
                3590.00m,
                Category.Outerwear,
                "https://static.pullandbear.net/assets/public/96f4/bf37/d40b4e719f23/19c1b1093451/07720317717-A6M/07720317717-A6M.jpg?ts=1783611243069&w=480&f=auto",
                waitDays: 30,
                savedAt: now.AddDays(-9)),
            Item(
                "https://www.muji.eu/products/high-quality-paper-slim-notebook-a6-10132",
                "High Quality Paper Slim Notebook A6",
                "Muji",
                165.00m,
                Category.Stationery,
                "https://cdn11.bigcommerce.com/s-36unquhwg5/images/stencil/1280x1280/products/5055/2652737/V-10132-H-002592__08716.1763484699.jpg?c=1",
                waitDays: 15,
                savedAt: now.AddDays(-2),
                priceIsEstimate: true),
            Item(
                "https://sllr.co/classicpink/pd/1742529?Sliver_necklace&src=/shop",
                "Silver necklace",
                "SLLR",
                350.00m,
                Category.Jewellery,
                "https://storage.googleapis.com/bosta-files/products_images/MTczODcwX18yMDI2LTA3LTI1VDE0OjIwOjA3LjQ4NFpfSU1HXzkyNTQuanBlZw==.jpeg",
                waitDays: 30,
                savedAt: now.AddDays(-50),
                status: ItemStatus.Bought,
                closedAt: now.AddDays(-8)),
            Item(
                "https://www.muji.eu/products/polypropylene-cable-case-with-stand-17204",
                "Polypropylene Cable Case with Stand",
                "Muji",
                600.00m,
                Category.Home,
                "https://cdn11.bigcommerce.com/s-36unquhwg5/images/stencil/1280x1280/products/6088/2588201/P-17204-H-000000-920999_06__43731.1780416763.jpg?c=1",
                waitDays: 30,
                savedAt: now.AddDays(-25),
                priceIsEstimate: true,
                status: ItemStatus.LetGo,
                closedAt: now.AddDays(-7)),
        ],
    };

    private static Item Item(
        string? url,
        string title,
        string? brand,
        decimal price,
        Category category,
        string? imageUrl,
        int waitDays,
        DateTimeOffset savedAt,
        bool priceIsEstimate = false,
        ItemStatus status = ItemStatus.Waiting,
        DateTimeOffset? closedAt = null) => new()
        {
            Url = url,
            Title = title,
            Brand = brand,
            ImageUrl = imageUrl,
            Price = price,
            PriceIsEstimate = priceIsEstimate,
            Currency = "EGP",
            Category = category,
            WaitDays = waitDays,
            SavedAt = savedAt,
            Status = status,
            ClosedAt = closedAt,
        };
}
