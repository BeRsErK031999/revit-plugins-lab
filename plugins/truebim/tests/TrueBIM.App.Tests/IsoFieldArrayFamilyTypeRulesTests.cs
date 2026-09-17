using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldArrayFamilyTypeRulesTests
{
    [Theory]
    [InlineData(10d, 200d, true)]
    [InlineData(10.01, 199.99, true)]
    [InlineData(100d, 200d, false)]
    [InlineData(10d, 2000d, false)]
    [InlineData(12d, 200d, false)]
    [InlineData(10d, 100d, false)]
    [InlineData(null, 200d, false)]
    [InlineData(10d, null, false)]
    [InlineData(double.NaN, 200d, false)]
    [InlineData(10d, double.PositiveInfinity, false)]
    public void SizeMatches_RequiresActualFiniteParameters(double? diameter, double? spacing, bool expected)
    {
        Assert.Equal(expected, IsoFieldArrayFamilyTypeRules.SizeMatches(diameter, spacing, 10, 200));
    }

    [Theory]
    [InlineData("Арматура ⌀10 A500С ГОСТ", 123d, true)]
    [InlineData("Арматура А500С", 123d, true)]
    [InlineData("A500C", 123d, true)]
    [InlineData("A400", 123d, false)]
    [InlineData("A500", 123d, false)]
    [InlineData("A500С0", 123d, false)]
    [InlineData("A500С A400", 123d, false)]
    [InlineData("Нет в сортаменте", 123d, false)]
    [InlineData(null, 123d, false)]
    [InlineData("A500С", null, false)]
    [InlineData("A500С", double.NaN, false)]
    public void HasVerifiedSteelGrade_RequiresLookupResultAndRealCode(string? productName, double? code, bool expected)
    {
        Assert.Equal(expected, IsoFieldArrayFamilyTypeRules.HasVerifiedSteelGrade(productName, code));
    }

    [Theory]
    [InlineData(1170d, 400d, 3d, true)]
    [InlineData(1170.01, 400.01, 3d, true)]
    [InlineData(1300d, 400d, 3d, false)]
    [InlineData(1170d, 200d, 3d, false)]
    [InlineData(1170d, 400d, 2d, false)]
    [InlineData(null, 400d, 3d, false)]
    [InlineData(1170d, null, 3d, false)]
    [InlineData(1170d, 400d, null, false)]
    public void GeometryMatches_RejectsFamilyFormulaChangesToPlannedEnvelope(
        double? length, double? width, double? count, bool expected)
    {
        Assert.Equal(expected, IsoFieldArrayFamilyTypeRules.GeometryMatches(length, width, count, 1170, 400, 3));
    }
}
