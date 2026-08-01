using Hold.Data;
using Hold.Services;

namespace Hold.Tests;

public class CategoryTests
{
    private const int ColumnLength = 20;

    [Fact]
    public void EveryNameFitsTheColumn()
    {
        // Category is stored HasConversion<string>().HasMaxLength(20), so the member name is the
        // value in the database. A longer one throws on insert with nothing catching it earlier.
        var tooLong = Enum.GetNames<Category>().Where(name => name.Length > ColumnLength).ToList();

        Assert.Empty(tooLong);
    }

    [Fact]
    public void OtherComesLast()
    {
        // CategoryStrip and the add panel's select both render Enum.GetValues in declaration order,
        // and Other is the fallback, so it belongs at the end of the strip rather than the middle.
        var names = Enum.GetNames<Category>();

        Assert.Equal(nameof(Category.Other), names[^1]);
    }

    [Fact]
    public void NamesAreDistinct()
    {
        var names = Enum.GetNames<Category>();

        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(Category.Accessories)]
    [InlineData(Category.Kitchen)]
    [InlineData(Category.Stationery)]
    [InlineData(Category.Books)]
    [InlineData(Category.Art)]
    [InlineData(Category.Projects)]
    public void ACategoryCanBeFiledOnAnItem(Category category) =>
        Assert.Null(ItemService.DescribeProblem(
            new ItemDraft(null, "A thing", null, null, 12m, "USD", category, 30, null)));
}
