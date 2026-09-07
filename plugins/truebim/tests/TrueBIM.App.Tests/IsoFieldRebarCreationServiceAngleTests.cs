using System.Reflection;
using TrueBIM.App.Modules.IsoFieldRebar.Revit;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRebarCreationServiceAngleTests
{
    [Theory]
    [InlineData(3 * Math.PI / 2, -Math.PI / 2)]
    [InlineData(-Math.PI / 2, 3 * Math.PI / 2)]
    [InlineData(Math.PI, 0)]
    [InlineData(0, Math.PI)]
    public void AngleMatchesModuloPi_TreatsEquivalentFamilyRotationsAsEqual(
        double actual,
        double expected)
    {
        Assert.True(AngleMatchesModuloPi(actual, expected));
    }

    [Theory]
    [InlineData(Math.PI / 2, 0)]
    [InlineData(Math.PI / 4, -Math.PI / 4)]
    public void AngleMatchesModuloPi_RejectsDifferentFamilyRotations(
        double actual,
        double expected)
    {
        Assert.False(AngleMatchesModuloPi(actual, expected));
    }

    private static bool AngleMatchesModuloPi(double actual, double expected)
    {
        MethodInfo method = typeof(IsoFieldRebarCreationService).GetMethod(
            "AngleMatchesModuloPi",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Angle comparison method was not found.");

        return (bool)(method.Invoke(null, new object[] { actual, expected })
            ?? throw new InvalidOperationException("Angle comparison returned no result."));
    }
}
