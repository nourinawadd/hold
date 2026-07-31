namespace Hold.Services;

public static class Currencies
{
    public static readonly IReadOnlyList<string> Known =
        ["EGP", "AED", "SAR", "KWD", "USD", "GBP", "EUR", "CAD", "AUD", "JPY"];

    public const string Default = "USD";

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
