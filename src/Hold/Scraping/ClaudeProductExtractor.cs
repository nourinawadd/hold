using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hold.Scraping;

/// <summary>
/// Reads a page's visible text with Claude when the four structured parsers came up empty.
///
/// <para>
/// This helps exactly one case: the page was fetched but publishes no structured data. It
/// cannot help when a shop answers 403 — there is no text to read. That path still falls
/// through to manual entry.
/// </para>
/// </summary>
public sealed class ClaudeProductExtractor : IProductExtractor
{
    private const string Model = "claude-opus-5";

    private const string Instructions = """
        You read the visible text of an online shop's product page and report what it says
        about the one product being sold.

        Report only what the page states about that product. Return null for anything the
        page does not state — a null is correct and useful, a guess is neither.

        Never take a price from a related product, a "customers also bought" strip, a
        shipping threshold, a discount banner, or a subscription offer. If several prices
        appear and you cannot tell which belongs to this product, return null for price.

        Give the price exactly as printed, including its separators and symbol. Do not
        convert, round, or reformat it. Give the currency as a three-letter code when the
        page makes it unambiguous, otherwise null.
        """;

    /// <summary>All four fields are required so the model must state a null rather than
    /// quietly omitting a key it was unsure about.</summary>
    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            title = Nullable("The product's name, as printed on the page."),
            brand = Nullable("The brand or maker, if the page names one."),
            price = Nullable("The current price exactly as printed, e.g. \"$1,234.56\" or \"1.234,56 €\"."),
            currency = Nullable("Three-letter currency code, e.g. USD, only if unambiguous."),
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "title", "brand", "price", "currency" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    private static readonly JsonSerializerOptions Lenient = new() { PropertyNameCaseInsensitive = true };

    private readonly AnthropicClient? client;
    private readonly ILogger<ClaudeProductExtractor> log;

    public ClaudeProductExtractor(string? apiKey, ILogger<ClaudeProductExtractor>? logger = null)
    {
        // No key configured is a normal state, not an error. Hold works without this.
        client = string.IsNullOrWhiteSpace(apiKey) ? null : new AnthropicClient { ApiKey = apiKey };
        log = logger ?? NullLogger<ClaudeProductExtractor>.Instance;
    }

    public bool Enabled => client is not null;

    public async Task<ProductDraft?> ExtractAsync(
        string pageText,
        Uri url,
        CancellationToken cancellationToken)
    {
        if (client is null || string.IsNullOrWhiteSpace(pageText))
        {
            return null;
        }

        var message = await client.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 1024,
            System = Instructions,
            OutputConfig = new OutputConfig
            {
                // Extraction from text in front of it — this is not a reasoning-heavy task,
                // and low effort keeps the per-scrape cost down.
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = Schema },
            },
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = $"Product page: {url}\n\n---\n{pageText}",
                },
            ],
        });

        // Safety classifiers can decline a request. Check before reading content — on a
        // refusal there is nothing in it.
        if (message.StopReason == "refusal")
        {
            log.LogWarning(
                "Claude declined to read {Host} ({Category}).",
                url.Host,
                message.StopDetails?.Category);

            return null;
        }

        var json = message.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProductDraft>(json, Lenient);
        }
        catch (JsonException exception)
        {
            // The schema makes this very unlikely, but a scrape must never throw — and a
            // swallowed parse failure that leaves no trace is how a silent regression lives
            // for months.
            log.LogWarning(exception, "Could not read Claude's reply for {Host}.", url.Host);

            return null;
        }
    }

    private static object Nullable(string description) => new
    {
        anyOf = new object[] { new { type = "string" }, new { type = "null" } },
        description,
    };
}
