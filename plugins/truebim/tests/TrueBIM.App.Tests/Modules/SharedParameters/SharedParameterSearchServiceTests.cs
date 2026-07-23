using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class SharedParameterSearchServiceTests
{
    private readonly SharedParameterSearchService service = new();

    [Theory]
    [InlineData("тестовый")]
    [InlineData("ТЕСТОВЫЙ ПАРАМЕТР")]
    [InlineData("3e04db00-0fa8-4aed-9fc3-8f47cb91ade1")]
    [InlineData("{3E04DB00-0FA8-4AED-9FC3-8F47CB91ADE1}")]
    [InlineData("0fa84aed")]
    public void Matches_AcceptsNameFullGuidAndPartialGuid(string query)
    {
        Assert.True(service.Matches(SharedParameterTestData.Parameter(), query));
    }

    [Fact]
    public void Filter_PreservesSameNameParametersWithDifferentGuids()
    {
        SharedParameterDescriptor first = SharedParameterTestData.Parameter(id: 1, name: "Дубликат");
        SharedParameterDescriptor second = SharedParameterTestData.Parameter(
            id: 2,
            name: "Дубликат",
            guid: Guid.Parse("f44ec74a-4eb5-4281-bdc1-85f50939c25d"));

        IReadOnlyList<SharedParameterDescriptor> result = service.Filter(
            [first, second],
            "дубликат",
            SharedParameterListFilter.All);

        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].IdentityKey, result[1].IdentityKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("не-guid")]
    [InlineData("3e04db00")]
    public void IsGuidInputValid_RejectsIncompleteOrMalformedInput(string value)
    {
        Assert.False(service.IsGuidInputValid(value));
    }

    [Fact]
    public void Filter_UsesCompletedAnalysisForUnusedState()
    {
        SharedParameterDescriptor unused = SharedParameterTestData.Parameter(id: 1, name: "Не используется");
        SharedParameterDescriptor used = SharedParameterTestData.Parameter(
            id: 2,
            name: "Используется",
            guid: Guid.Parse("f44ec74a-4eb5-4281-bdc1-85f50939c25d"));
        ElementParameterUsage usage = new(
            300,
            "uid-300",
            "Стена",
            "Стены",
            string.Empty,
            string.Empty,
            false,
            true,
            true,
            false,
            "A");
        Dictionary<string, SharedParameterProjectAnalysis> analyses = new()
        {
            [unused.IdentityKey] = SharedParameterTestData.Analysis(unused),
            [used.IdentityKey] = SharedParameterTestData.Analysis(used, elements: [usage])
        };

        SharedParameterDescriptor result = Assert.Single(service.Filter(
            [used, unused],
            null,
            SharedParameterListFilter.Unused,
            analyses));

        Assert.Equal(unused.IdentityKey, result.IdentityKey);
    }
}
