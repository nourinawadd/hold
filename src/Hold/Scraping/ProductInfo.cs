namespace Hold.Scraping;

public sealed record ProductInfo(
    string Url,
    string? Title,
    string? Brand,
    string? ImageUrl,
    decimal? Price,
    string? Currency,
    IReadOnlyList<string> ReadFrom);

public enum ProductField
{
    Title,
    Brand,
    ImageUrl,
    Price,
    Currency,
}

public sealed record ScrapeOutcome(
    ProductInfo Info,
    IReadOnlyDictionary<ProductField, string> Sources,
    string? Message)
{
    private static readonly HashSet<string> Strong = [ShopifyName, JsonLdName];

    public const string ShopifyName = "Shopify";
    public const string JsonLdName = "JSON-LD";
    public const string OpenGraphName = "page metadata";
    public const string MicrodataName = "microdata";

    public const string LlmName = "Claude";

    public const string UrlName = "the link";

    public bool IsUnverified(ProductField field) =>
        Sources.TryGetValue(field, out var source) && !Strong.Contains(source);

    public bool Has(ProductField field) => Sources.ContainsKey(field);

    public string Provenance() =>
        Info.ReadFrom.Count == 0
            ? "Nothing could be read"
            : $"Read from {string.Join(" and ", Info.ReadFrom)}";
}

public sealed record ScrapeStep(string Text, TimeSpan? Elapsed = null);
