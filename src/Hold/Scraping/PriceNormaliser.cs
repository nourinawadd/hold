using System.Globalization;

namespace Hold.Scraping;

/// <summary>
/// Turns whatever a shop prints into a decimal. Handles "$1,234.56", "1.234,56 €",
/// "USD 89.00", and bare numbers.
/// </summary>
public static class PriceNormaliser
{
    private static readonly Dictionary<char, string> SymbolCurrencies = new()
    {
        ['$'] = "USD",
        ['€'] = "EUR",
        ['£'] = "GBP",
        ['¥'] = "JPY",
    };

    /// <summary>
    /// The rule that decides what a separator means: whichever of comma or dot appears
    /// <em>last</em> is the decimal separator, because no notation puts a thousands
    /// separator after the decimal point. When only one kind is present it is ambiguous —
    /// "1.234" is a thousand in Berlin and one-and-a-bit in Boston — and a single separator
    /// with exactly three digits behind it is read as thousands.
    /// </summary>
    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Keep only what can belong to a number. Drops currency symbols, codes, and the
        // spaces some locales use to group thousands.
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray());

        if (cleaned.Length == 0 || !cleaned.Any(char.IsDigit))
        {
            return null;
        }

        // A minus is only meaningful at the front.
        var negative = cleaned.StartsWith('-');
        cleaned = cleaned.Replace("-", string.Empty);

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        string normalised;

        if (lastComma >= 0 && lastDot >= 0)
        {
            // Both present: the later one is the decimal separator, the other is grouping.
            normalised = lastComma > lastDot
                ? cleaned.Replace(".", string.Empty).Replace(',', '.')
                : cleaned.Replace(",", string.Empty);

            normalised = KeepLastSeparatorOnly(normalised);
        }
        else if (lastComma >= 0)
        {
            normalised = Disambiguate(cleaned, ',');
        }
        else if (lastDot >= 0)
        {
            normalised = Disambiguate(cleaned, '.');
        }
        else
        {
            normalised = cleaned;
        }

        if (!decimal.TryParse(normalised, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return negative ? -value : value;
    }

    /// <summary>Reads a currency off the same string, for shops that print "USD 89.00".</summary>
    public static string? Currency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var (symbol, code) in SymbolCurrencies)
        {
            if (raw.Contains(symbol))
            {
                return code;
            }
        }

        // A bare three-letter run, e.g. "USD 89.00" or "89.00 EUR".
        var letters = new string(raw.Where(char.IsLetter).ToArray());

        return letters.Length == 3 ? letters.ToUpperInvariant() : null;
    }

    private static string Disambiguate(string cleaned, char separator)
    {
        var parts = cleaned.Split(separator);

        // More than one of the same separator can only be grouping: 1.234.567
        if (parts.Length > 2)
        {
            return cleaned.Replace(separator.ToString(), string.Empty);
        }

        var tail = parts[^1];

        // Exactly three trailing digits is the ambiguous case, and grouping is the safer
        // reading: shops write 1,200 far more often than they mean 1.2 written oddly.
        if (tail.Length == 3)
        {
            return cleaned.Replace(separator.ToString(), string.Empty);
        }

        return separator == ',' ? cleaned.Replace(',', '.') : cleaned;
    }

    /// <summary>Collapses any grouping separators left over, keeping the decimal point.</summary>
    private static string KeepLastSeparatorOnly(string value)
    {
        var lastDot = value.LastIndexOf('.');

        if (lastDot < 0)
        {
            return value;
        }

        return value[..lastDot].Replace(".", string.Empty) + "." + value[(lastDot + 1)..];
    }
}
