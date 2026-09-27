using TrueBIM.App.Modules.IsoFieldRebar.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldHostElementTests
{
    [Fact]
    public void DisplayName_IncludesHostKindNameAndElementId()
    {
        IsoFieldHostElement host = new(
            12345,
            "Wall",
            "Стена",
            "Basic Wall 200");

        Assert.Equal("Стена: Basic Wall 200 (элемент № 12345)", host.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToElementIdWhenNameIsEmpty()
    {
        IsoFieldHostElement host = new(
            67890,
            "Slab",
            "Плита",
            string.Empty);

        Assert.Equal("Плита (элемент № 67890)", host.DisplayName);
    }
}
