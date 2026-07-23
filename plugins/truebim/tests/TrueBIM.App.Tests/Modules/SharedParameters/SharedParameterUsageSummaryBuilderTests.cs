using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class SharedParameterUsageSummaryBuilderTests
{
    private readonly SharedParameterUsageSummaryBuilder builder = new();

    [Fact]
    public void BuildElementAggregates_GroupsFilledEmptyAndReadOnlyValues()
    {
        IReadOnlyList<ElementParameterUsage> usages =
        [
            Usage(1, "Стены", hasValue: true, isReadOnly: false),
            Usage(2, "Стены", hasValue: false, isReadOnly: true),
            Usage(3, "Двери", hasValue: true, isReadOnly: false)
        ];

        IReadOnlyList<ElementUsageAggregate> result = builder.BuildElementAggregates(usages);

        ElementUsageAggregate walls = Assert.Single(result, item => item.CategoryName == "Стены");
        Assert.Equal(2, walls.ElementCount);
        Assert.Equal(1, walls.FilledCount);
        Assert.Equal(1, walls.EmptyCount);
        Assert.Equal(1, walls.ReadOnlyCount);
    }

    [Fact]
    public void DeduplicateElements_UsesElementIdAsStableIdentity()
    {
        IReadOnlyList<ElementParameterUsage> result = builder.DeduplicateElements(
            [Usage(10, "Стены", true, false), Usage(10, "Стены", false, true)]);

        Assert.Single(result);
        Assert.True(result[0].HasValue);
    }

    private static ElementParameterUsage Usage(
        long id,
        string category,
        bool hasValue,
        bool isReadOnly)
    {
        return new ElementParameterUsage(
            id,
            $"uid-{id}",
            $"Элемент {id}",
            category,
            string.Empty,
            string.Empty,
            false,
            true,
            hasValue,
            isReadOnly,
            hasValue ? "Значение" : string.Empty);
    }
}
