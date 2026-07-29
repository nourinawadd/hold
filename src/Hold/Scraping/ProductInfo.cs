namespace Hold.Scraping;

/// <summary>What a parser recovered from a page. Every field is optional: partial recovery
/// is the normal outcome, not the exceptional one.</summary>
public sealed record ProductInfo(
    string Url,
    string? Title,
    string? Brand,
    string? ImageUrl,
    decimal? Price,
    string? Currency,
    IReadOnlyList<string> ReadFrom);

/// <summary>The fields a parser can supply, so the UI can say where each one came from.</summary>
public enum ProductField
{
    Title,
    Brand,
    ImageUrl,
    Price,
    Currency,
}

/// <summary>
/// A scrape, and enough provenance to render it honestly. <see cref="ProductInfo.ReadFrom"/>
/// names the strategies that contributed at all; <see cref="Sources"/> says which one
/// supplied each individual field, because a title from Shopify's JSON and a title guessed
/// from &lt;title&gt; deserve different weight on screen.
/// </summary>
/// <param name="Message">
/// Set when the shop refused or the page could not be read. Never an exception — the add
/// flow must always fall through to manual entry.
/// </param>
public sealed record ScrapeOutcome(
    ProductInfo Info,
    IReadOnlyDictionary<ProductField, string> Sources,
    string? Message)
{
    /// <summary>Strategies whose values are worth showing in full ink.</summary>
    private static readonly HashSet<string> Strong = [ShopifyName, JsonLdName];

    public const string ShopifyName = "Shopify";
    public const string JsonLdName = "JSON-LD";
    public const string OpenGraphName = "page metadata";
    public const string MicrodataName = "microdata";

    /// <summary>
    /// True when the value shown for this field is a guess rather than a reading — a title
    /// scraped from og:title, say. Those render faint.
    /// </summary>
    public bool IsUnverified(ProductField field) =>
        Sources.TryGetValue(field, out var source) && !Strong.Contains(source);

    public bool Has(ProductField field) => Sources.ContainsKey(field);

    /// <summary>
    /// The panel header: READ FROM SHOPIFY, READ FROM PAGE METADATA. Naming the source is
    /// what prompts the user to check a scraped price, which lies about sales and variants.
    /// </summary>
    public string Provenance() =>
        Info.ReadFrom.Count == 0
            ? "Nothing could be read"
            : $"Read from {string.Join(" and ", Info.ReadFrom)}";
}

/// <summary>One line in the reading state. Real steps, not a fake spinner.</summary>
public sealed record ScrapeStep(string Text, TimeSpan? Elapsed = null);
