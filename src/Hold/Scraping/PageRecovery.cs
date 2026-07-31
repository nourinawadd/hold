using System.Text.Json;

namespace Hold.Scraping;

public sealed record RecoveredPage(string Html, string Via);

public sealed class PageRecovery(HttpClient http)
{
    private static readonly string[] ChallengeMarkers =
    [
        "bm-verify",
        "/_sec/verify",
        "__cf_chl",
        "cf-browser-verification",
        "Checking your browser",
        "px-captcha",
        "/_Incapsula_Resource",
        "distil_r_captcha",
    ];

    public static bool IsChallenge(string html) =>
        html.Length < 20_000
        && ChallengeMarkers.Any(marker => html.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<Uri> Variants(Uri url)
    {
        var otherHost = OtherHost(url.Host);

        if (otherHost is not null)
        {
            yield return new UriBuilder(url) { Host = otherHost }.Uri;
        }

        if (url.Query.Length > 0)
        {
            yield return new UriBuilder(url) { Query = string.Empty }.Uri;

            if (otherHost is not null)
            {
                yield return new UriBuilder(url) { Host = otherHost, Query = string.Empty }.Uri;
            }
        }

        var path = url.AbsolutePath;

        if (path.Length > 1)
        {
            var flipped = path.EndsWith('/') ? path.TrimEnd('/') : path + "/";
            yield return new UriBuilder(url) { Path = flipped }.Uri;
        }
    }

    private static string? OtherHost(string host)
    {
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return host[4..];
        }

        return host.Count(character => character == '.') == 1 ? "www." + host : null;
    }

    public async Task<RecoveredPage?> TryVariantsAsync(
        Uri url,
        IProgress<ScrapeStep>? progress,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string> { url.ToString() };

        foreach (var variant in Variants(url))
        {
            if (!seen.Add(variant.ToString()))
            {
                continue;
            }

            progress?.Report(new ScrapeStep($"Retrying as {Describe(url, variant)}"));

            var html = await FetchAsync(variant, cancellationToken);

            if (html is not null)
            {
                return new RecoveredPage(html, $"a different address ({Describe(url, variant)})");
            }
        }

        return null;
    }

    public async Task<RecoveredPage?> TryArchiveAsync(
        Uri url,
        IProgress<ScrapeStep>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ScrapeStep("Asking the Wayback Machine"));

        string? snapshot;

        try
        {
            var lookup = "https://archive.org/wayback/available?url="
                + Uri.EscapeDataString(url.Host + url.AbsolutePath);

            using var response = await http.GetAsync(lookup, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            snapshot = document.RootElement
                .TryGetProperty("archived_snapshots", out var snapshots)
                && snapshots.TryGetProperty("closest", out var closest)
                && closest.TryGetProperty("url", out var location)
                    ? location.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }

        if (snapshot is null || !Uri.TryCreate(snapshot, UriKind.Absolute, out var snapshotUrl))
        {
            return null;
        }

        var html = await FetchAsync(snapshotUrl, cancellationToken);

        return html is null ? null : new RecoveredPage(html, "an archived copy");
    }

    private async Task<string?> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;

            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            return IsChallenge(html) ? null : html;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static string Describe(Uri original, Uri variant)
    {
        var hostChanged = !variant.Host.Equals(original.Host, StringComparison.OrdinalIgnoreCase);
        var queryDropped = variant.Query.Length == 0 && original.Query.Length > 0;

        return (hostChanged, queryDropped) switch
        {
            (true, true) => $"{variant.Host} without tracking",
            (true, false) => variant.Host,
            (false, true) => "a plain link",
            _ => "a different path",
        };
    }
}
