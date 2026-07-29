using System.Text.Json;

namespace Hold.Scraping;

/// <summary>A page, and how it was got. The label becomes a reading step.</summary>
public sealed record RecoveredPage(string Html, string Via);

/// <summary>
/// What to try when the plain request does not produce a readable page. Each attempt is
/// independent and cheap; they run in order until one returns markup or all have failed.
///
/// <para>
/// Deliberately excluded: solving a shop's bot challenge, and any path the site disallows
/// in robots.txt. Pull&amp;Bear's product API answers perfectly and is
/// <c>Disallow: /itxrest</c> — that is a no, and this respects it. Where a shop blocks
/// readers outright, <see cref="UrlSlugParser"/> is the answer instead.
/// </para>
/// </summary>
public sealed class PageRecovery(HttpClient http)
{
    /// <summary>
    /// Fingerprints of the major bot managers' challenge pages. Matched on markers rather
    /// than page size: a short page can be a real product page, and judging by length alone
    /// threw away perfectly good minimal markup.
    /// </summary>
    private static readonly string[] ChallengeMarkers =
    [
        "bm-verify",              // Akamai Bot Manager — what Pull&Bear and Zara serve
        "/_sec/verify",
        "__cf_chl",               // Cloudflare
        "cf-browser-verification",
        "Checking your browser",
        "px-captcha",             // PerimeterX
        "/_Incapsula_Resource",   // Imperva
        "distil_r_captcha",
    ];

    /// <summary>
    /// True when a 200 is a checkpoint rather than the page. Size alone is not the signal —
    /// the marker is, and the size bound only stops a long page that merely mentions one.
    /// </summary>
    public static bool IsChallenge(string html) =>
        html.Length < 20_000
        && ChallengeMarkers.Any(marker => html.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Same page, different spellings. A shop that 404s the apex may serve www, and a
    /// tracking parameter can be the whole reason a request is refused.
    /// </summary>
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

    /// <summary>
    /// The www/apex counterpart, or null when there isn't a sensible one. "www2.hm.com" is
    /// a real subdomain, not a www-prefixed apex — prepending another "www." to it was a
    /// wasted request to a host that has never existed.
    /// </summary>
    private static string? OtherHost(string host)
    {
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return host[4..];
        }

        // Only an apex gets a www. added; anything already subdomained is left alone.
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

    /// <summary>
    /// The Wayback Machine's last copy. Fine for a name and an image, and the reason price
    /// is never taken from it: an archived price is a price from some other month.
    /// </summary>
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

    /// <summary>Swallows everything. A recovery attempt that throws is worse than one that
    /// fails, because the next attempt never gets its turn.</summary>
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

            // A bot-challenge interstitial answers 200 with a page of challenge script.
            // Treating that as the product page wastes every later step.
            return IsChallenge(html) ? null : html;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Names what actually differs. Two attempts that change different things must not
    /// print the same line — the reading state is a record of what was tried.
    /// </summary>
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
