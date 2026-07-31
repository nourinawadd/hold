namespace Hold.Tests;

public class PriceNormaliserTests
{
    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("1.234,56 €", 1234.56)]
    [InlineData("USD 89.00", 89.00)]
    [InlineData("89", 89)]
    [InlineData("1,234,567.89", 1234567.89)]
    [InlineData("1.234.567,89", 1234567.89)]
    [InlineData("1,23", 1.23)]
    [InlineData("1.23", 1.23)]
    [InlineData("1,234", 1234)]
    [InlineData("1.234", 1234)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("  Sale price $1 234,56  ", 1234.56)]
    [InlineData("Now £45", 45)]
    [InlineData("45.00 GBP", 45.00)]
    public void Reads(string raw, double expected) =>
        Assert.Equal((decimal)expected, PriceNormaliser.Parse(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sold out")]
    [InlineData("$")]
    public void RejectsWhatIsNotAPrice(string? raw) =>
        Assert.Null(PriceNormaliser.Parse(raw));

    [Fact]
    public void KeepsScale()
    {
        Assert.Equal(890.00m, PriceNormaliser.Parse("$890.00"));
        Assert.Equal(0.99m, PriceNormaliser.Parse("0.99"));
    }

    [Theory]
    [InlineData("$1,234.56", "USD")]
    [InlineData("1.234,56 €", "EUR")]
    [InlineData("£45", "GBP")]
    [InlineData("USD 89.00", "USD")]
    [InlineData("89.00 eur", "EUR")]
    public void ReadsCurrency(string raw, string expected) =>
        Assert.Equal(expected, PriceNormaliser.Currency(raw));

    [Theory]
    [InlineData("89.00")]
    [InlineData("1,234")]
    [InlineData(null)]
    public void LeavesCurrencyAloneWhenAbsent(string? raw) =>
        Assert.Null(PriceNormaliser.Currency(raw));
}
