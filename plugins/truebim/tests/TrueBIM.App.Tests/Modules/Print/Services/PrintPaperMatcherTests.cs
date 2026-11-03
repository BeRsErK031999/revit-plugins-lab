using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintPaperMatcherTests
{
    private static readonly PrinterPaper[] Papers =
    [
        new("A4", new(210.1, 296.9)), new("A3", new(296.9, 420.1)),
        new("A2", new(420, 594)), new("D", new(609.6, 914.4)), new("E", new(914.4, 1219.2))
    ];

    [Theory]
    [InlineData(210, 297, "A4", false, true)]
    [InlineData(420, 297, "A3", true, true)]
    [InlineData(891, 420, "D", true, false)]
    [InlineData(420, 891, "D", false, false)]
    public void FitsBothSidesAtActualSize(double width, double height, string expected, bool rotated, bool exact)
    {
        var match = PrintPaperMatcher.Find(new(width, height), Papers.Reverse());
        Assert.NotNull(match);
        Assert.Equal(expected, match.Paper.Name);
        Assert.Equal(rotated, match.Rotate);
        Assert.Equal(exact, match.IsExact);
    }

    [Fact]
    public void RejectsOversizeSheetEvenWhenSmallerPaperHasEnoughArea()
    {
        Assert.Null(PrintPaperMatcher.Find(new(1400, 200), Papers));
    }
}
