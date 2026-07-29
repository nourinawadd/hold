using System.Text;
using AngleSharp.Dom;

namespace Hold.Scraping;

/// <summary>
/// Last in the chain. Turns the page into plain text and asks a model what the product is,
/// for shops that publish no structured data at all.
///
/// <para>
/// A guess, not a reading — <see cref="StrongSource"/> is false, so everything it supplies
/// renders faint and waits to be confirmed.
/// </para>
/// </summary>
public sealed class LlmParser(IProductExtractor extractor) : IProductParser
{
    /// <summary>Roughly four thousand tokens. Product facts sit near the top of a page;
    /// the rest is footer, reviews, and recommendations that only invite bad guesses.</summary>
    private const int MaxCharacters = 12_000;

    /// <summary>Elements whose text is markup, not page content.</summary>
    private static readonly HashSet<string> Ignored =
        ["script", "style", "noscript", "svg", "iframe", "template", "head"];

    public string Name => ScrapeOutcome.LlmName;

    /// <summary>Never. A model reading prose is the weakest source in the chain.</summary>
    public bool StrongSource => false;

    public async Task<ProductInfo?> TryParseAsync(
        ScrapeContext context,
        CancellationToken cancellationToken)
    {
        if (context.Document is null || !extractor.Enabled)
        {
            return null;
        }

        var text = PageText(context.Document);

        if (text.Length == 0)
        {
            return null;
        }

        var draft = await extractor.ExtractAsync(text, context.Url, cancellationToken);

        if (draft is null)
        {
            return null;
        }

        var price = PriceNormaliser.Parse(draft.Price);

        var info = new ProductInfo(
            context.Url.ToString(),
            Blank(draft.Title),
            Blank(draft.Brand),
            // Images are left to the other strategies: picking one out of prose is a guess
            // with no way to check it.
            null,
            price,
            Blank(draft.Currency) ?? PriceNormaliser.Currency(draft.Price),
            [ScrapeOutcome.LlmName]);

        var empty = info is { Title: null, Brand: null, Price: null, Currency: null };

        return empty ? null : info;
    }

    /// <summary>
    /// Visible text only. Walks the tree rather than stripping nodes, because the document
    /// is shared with the parsers that ran before this one and must not be mutated.
    /// </summary>
    private static string PageText(IDocument document)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(document.Title))
        {
            builder.Append("PAGE TITLE: ").AppendLine(document.Title.Trim());
        }

        var description = document.QuerySelector("meta[name='description']")?.GetAttribute("content");

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append("DESCRIPTION: ").AppendLine(description.Trim());
        }

        builder.AppendLine();

        var lastWasSpace = true;

        foreach (var fragment in Text(document.Body))
        {
            foreach (var character in fragment)
            {
                var space = char.IsWhiteSpace(character);

                if (space && lastWasSpace)
                {
                    continue;
                }

                builder.Append(space ? ' ' : character);
                lastWasSpace = space;

                if (builder.Length >= MaxCharacters)
                {
                    return builder.ToString();
                }
            }

            // A boundary between elements is a word break even without whitespace.
            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static IEnumerable<string> Text(INode? node)
    {
        if (node is null)
        {
            yield break;
        }

        if (node is IText text)
        {
            yield return text.Data;
            yield break;
        }

        if (node is IElement element && Ignored.Contains(element.LocalName))
        {
            yield break;
        }

        foreach (var child in node.ChildNodes)
        {
            foreach (var fragment in Text(child))
            {
                yield return fragment;
            }
        }
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
