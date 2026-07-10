using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewSourceResolverTests
{
    [Theory]
    [InlineData("BIM_Opening_Door_123_Family_Type", 123)]
    [InlineData("BIM_Opening_Window_987", 987)]
    [InlineData("BIM_Opening_456", 456)]
    public void TryExtractElementId_ReadsDefaultOpeningViewNames(string viewName, long expected)
    {
        bool parsed = OpeningViewSourceResolver.TryExtractElementId(viewName, out long elementId);

        Assert.True(parsed);
        Assert.Equal(expected, elementId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Facade_Door_123")]
    [InlineData("BIM_Opening_Door_without_id")]
    public void TryExtractElementId_RejectsUnrelatedOrAmbiguousNames(string viewName)
    {
        Assert.False(OpeningViewSourceResolver.TryExtractElementId(viewName, out _));
    }
}
