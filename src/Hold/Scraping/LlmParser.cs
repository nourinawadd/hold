using System.Text;
using AngleSharp.Dom;

namespace Hold.Scraping;

public sealed class LlmParser(IProductExtractor extractor) : IProductParser
{
    private const int MaxCharacters = 12_000;

    private static readonly HashSet<string> Ignored =
        ["script", "style", "noscript", "svg", "iframe", "template", "head"];

    public string Name => ScrapeOutcome.LlmName;

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
            null,
            price,
            Blank(draft.Currency) ?? PriceNormaliser.Currency(draft.Price),
            [ScrapeOutcome.LlmName]);

        var empty = info is { Title: null, Brand: null, Price: null, Currency: null };

        return empty ? null : info;
    }

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
