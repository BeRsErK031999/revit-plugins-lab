using TrueBIM.App.Modules.Lintels.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelCandidateMatcherTests
{
    [Theory]
    [InlineData("Перемычки монолитные", "ПР-1", null)]
    [InlineData("Generic Model", "Lintel PR-2", null)]
    [InlineData("Обобщенная модель", "ПР-3", "Перемычка над дверью")]
    public void IsMatch_AcceptsKnownNameFragments(
        string familyName,
        string typeName,
        string? instanceName)
    {
        Assert.True(LintelCandidateMatcher.IsMatch(familyName, typeName, instanceName));
    }

    [Fact]
    public void IsMatch_RejectsUnrelatedNames()
    {
        Assert.False(LintelCandidateMatcher.IsMatch("Обобщенная модель", "ПР-1", "Элемент 1"));
    }
}
