namespace Hold.Tests;

public class UrlNormaliserTests
{
    private const string Clean = "https://shopdoen.com/products/long-scoop-neck-slip-black-2";

    [Theory]
    [InlineData("?utm_source=instagram")]
    [InlineData("?utm_source=x&utm_medium=y&utm_campaign=z")]
    [InlineData("?gclid=abc123")]
    [InlineData("?fbclid=abc123")]
    [InlineData("?msclkid=abc123")]
    [InlineData("?srsltid=abc123")]
    [InlineData("?mc_cid=abc&mc_eid=def")]
    [InlineData("#reviews")]
    [InlineData("?utm_source=x&gclid=y#reviews")]
    public void StripsTracking(string suffix) =>
        Assert.Equal(Clean, UrlNormaliser.Normalise(Clean + suffix));

    [Fact]
    public void KeepsParametersThatIdentifyTheProduct()
    {
        // ?variant= picks the colour and size. Dropping it would save the wrong thing.
        Assert.Equal(
            $"{Clean}?variant=42553859211360",
            UrlNormaliser.Normalise($"{Clean}?variant=42553859211360&utm_source=instagram#reviews"));
    }

    [Fact]
    public void SurvivesRepeatedNormalisation()
    {
        var once = UrlNormaliser.Normalise($"{Clean}?utm_source=x#frag");

        Assert.Equal(once, UrlNormaliser.Normalise(once));
    }

    [Fact]
    public void LeavesSomethingThatIsNotAUrlAlone() =>
        Assert.Equal("not a url", UrlNormaliser.Normalise("  not a url  "));

    [Fact]
    public void IsCaseInsensitiveAboutParameterNames() =>
        Assert.Equal(Clean, UrlNormaliser.Normalise($"{Clean}?UTM_Source=x&GCLID=y"));
}
