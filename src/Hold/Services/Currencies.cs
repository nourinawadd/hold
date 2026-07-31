namespace Hold.Services;

/// <summary>
/// The currencies offered in the UI.
///
/// <para>
/// Deliberately short. A typed three-letter code is a value nothing validates and everything
/// trusts — "eur", "USDS", or an empty string all reach the database, and a currency that does
/// not match the budget's is silently excluded from the bar rather than reported. A list this
/// size removes the whole class of mistake.
/// </para>
///
/// <para>
/// Codes only, with no display names: the select is sized for three characters, so a name
/// beside one is cut off mid-word rather than read.
/// </para>
///
/// <para>
/// Not an enum, and not a constraint on the column: prices are stored in whatever currency the
/// shop quoted, and a scraper reading a French shop will legitimately produce EUR. Adding one
/// is a line here.
/// </para>
/// </summary>
public static class Currencies
{
    /// <summary>
    /// Ordered by likelihood rather than alphabetically — the region first, then the currencies
    /// the shops this app scrapes actually quote in. A dropdown is only an improvement on typing
    /// while it stays short enough to scan.
    /// </summary>
    public static readonly IReadOnlyList<string> Known =
        ["EGP", "AED", "SAR", "KWD", "USD", "GBP", "EUR", "CAD", "AUD", "JPY"];

    public const string Default = "USD";

    /// <summary>
    /// The list to render, with <paramref name="current"/> appended when it is not already in
    /// it. Without this a select would quietly rewrite a scraped EUR price to the first option
    /// the moment somebody opened the form — changing the meaning of a number they never
    /// touched.
    /// </summary>
    public static IReadOnlyList<string> Including(string? current)
    {
        var code = current?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(code) || Known.Contains(code))
        {
            return Known;
        }

        return [.. Known, code];
    }
}
