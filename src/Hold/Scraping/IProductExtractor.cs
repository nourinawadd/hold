namespace Hold.Scraping;

public sealed record ProductDraft(string? Title, string? Brand, string? Price, string? Currency);

public interface IProductExtractor
{
    bool Enabled { get; }

    Task<ProductDraft?> ExtractAsync(string pageText, Uri url, CancellationToken cancellationToken);
}

public sealed class NoExtractor : IProductExtractor
{
    public static readonly NoExtractor Instance = new();

    public bool Enabled => false;

    public Task<ProductDraft?> ExtractAsync(string pageText, Uri url, CancellationToken cancellationToken) =>
        Task.FromResult<ProductDraft?>(null);
}
