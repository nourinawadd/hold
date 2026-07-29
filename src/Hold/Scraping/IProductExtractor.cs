namespace Hold.Scraping;

/// <summary>What a model read off the page. Strings, because the numbers still go through
/// <see cref="PriceNormaliser"/> — that logic is already written and already tested.</summary>
public sealed record ProductDraft(string? Title, string? Brand, string? Price, string? Currency);

/// <summary>
/// Reads product facts out of plain page text. Behind an interface so the scraper can be
/// tested without a network call, an API key, or a bill.
/// </summary>
public interface IProductExtractor
{
    /// <summary>
    /// False when no API key is configured. The chain skips the step entirely rather than
    /// reporting a reading step that will never produce anything.
    /// </summary>
    bool Enabled { get; }

    Task<ProductDraft?> ExtractAsync(string pageText, Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// The default. Hold's scraper is complete without a model, so no extractor means the
/// four structured strategies and nothing else.
/// </summary>
public sealed class NoExtractor : IProductExtractor
{
    public static readonly NoExtractor Instance = new();

    public bool Enabled => false;

    public Task<ProductDraft?> ExtractAsync(string pageText, Uri url, CancellationToken cancellationToken) =>
        Task.FromResult<ProductDraft?>(null);
}
