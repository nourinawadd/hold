using System.Globalization;

namespace Hold.Scraping;

public static class PriceNormaliser
{
    private static readonly Dictionary<char, string> SymbolCurrencies = new()
    {
        ['$'] = "USD",
        ['€'] = "EUR",
        ['£'] = "GBP",
        ['¥'] = "JPY",
    };

    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray());

        if (cleaned.Length == 0 || !cleaned.Any(char.IsDigit))
        {
            return null;
        }

        var negative = cleaned.StartsWith('-');
        cleaned = cleaned.Replace("-", string.Empty);

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        string normalised;

        if (lastComma >= 0 && lastDot >= 0)
        {
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

        var letters = new string(raw.Where(char.IsLetter).ToArray());

        return letters.Length == 3 ? letters.ToUpperInvariant() : null;
    }

    private static string Disambiguate(string cleaned, char separator)
    {
        var parts = cleaned.Split(separator);

        if (parts.Length > 2)
        {
            return cleaned.Replace(separator.ToString(), string.Empty);
        }

        var tail = parts[^1];

        if (tail.Length == 3)
        {
            return cleaned.Replace(separator.ToString(), string.Empty);
        }

        return separator == ',' ? cleaned.Replace(',', '.') : cleaned;
    }

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
