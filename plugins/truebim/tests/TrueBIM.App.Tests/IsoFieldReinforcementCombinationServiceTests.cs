using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldReinforcementCombinationServiceTests
{
    private readonly IsoFieldReinforcementCombinationService service = new();

    [Fact]
    public void TryParse_CalculatesAreaForPkLiraCombination()
    {
        bool parsed = service.TryParse(
            "d10s200+d14s200",
            out IsoFieldReinforcementCombination? combination,
            out string diagnostic);

        Assert.True(parsed, diagnostic);
        Assert.NotNull(combination);
        Assert.Equal(2, combination.Components.Count);
        Assert.Equal(11.624, combination.AreaSquareCentimetersPerMeter, 3);
        Assert.True(combination.Components[0].IsBase);
        Assert.False(combination.Components[1].IsBase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("d10@200")]
    [InlineData("d10s20")]
    public void TryParse_RejectsIncompleteOrUnsafeLabels(string? label)
    {
        bool parsed = service.TryParse(
            label,
            out IsoFieldReinforcementCombination? combination,
            out string diagnostic);

        Assert.False(parsed);
        Assert.Null(combination);
        Assert.NotEmpty(diagnostic);
    }
}
