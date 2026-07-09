using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ColorByParameter;

public sealed class ApplicableCategoryFilterTests
{
    [Fact]
    public void GetApplicableCategoryIdsKeepsAllCategoriesWhenParameterHasNoRestrictions()
    {
        IReadOnlyList<long> result = ApplicableCategoryFilter.GetApplicableCategoryIds(
            [300, 100, 300],
            []);

        Assert.Equal(new long[] { 300, 100 }, result);
    }

    [Fact]
    public void GetApplicableCategoryIdsReturnsOnlyParameterCategories()
    {
        IReadOnlyList<long> result = ApplicableCategoryFilter.GetApplicableCategoryIds(
            [300, 100, 200],
            [100, 400]);

        Assert.Equal(new long[] { 100 }, result);
    }
}
