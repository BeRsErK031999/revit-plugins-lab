using TrueBIM.App.Modules.SharedParameters.Revit;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class SharedParameterVersionAdapterTests
{
    [Fact]
    public void CanResolveLegacyParameterGroup_ValidBuiltInGroup_ReturnsTrue()
    {
        bool result = SharedParameterVersionAdapter.CanResolveLegacyParameterGroup(
            rawValue: -5000175,
            isDefined: true);

        Assert.True(result);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(int.MaxValue, false)]
    public void CanResolveLegacyParameterGroup_InvalidOrUndefinedGroup_ReturnsFalse(
        int rawValue,
        bool isDefined)
    {
        bool result = SharedParameterVersionAdapter.CanResolveLegacyParameterGroup(
            rawValue,
            isDefined);

        Assert.False(result);
    }
}
