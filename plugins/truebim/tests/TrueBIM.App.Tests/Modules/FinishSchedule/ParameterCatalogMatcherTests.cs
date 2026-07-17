using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class ParameterCatalogMatcherTests
{
    private static readonly ParameterCategoryReference Rooms = new(1, "Помещения");
    private static readonly ParameterCategoryReference Walls = new(2, "Стены");
    private static readonly ParameterCategoryReference Floors = new(3, "Перекрытия");

    [Fact]
    public void Evaluate_ExplainsEveryCompatibilityFailure()
    {
        ParameterCatalogItem item = new(
            ParameterReference.Project(
                "Неподходящий",
                10,
                ParameterBindingKind.Type,
                ParameterStorageKind.Integer),
            [Walls.Id],
            sampleCount: 2,
            writableSampleCount: 1,
            readOnlySampleCount: 1);
        ParameterCatalogRequirement requirement = new(
            "Выходное описание",
            ParameterBindingKind.Instance,
            [ParameterStorageKind.String],
            [Rooms, Floors],
            requireWritable: true);

        ParameterCompatibilityResult result = new ParameterCatalogMatcher().Evaluate(item, requirement);

        Assert.False(result.IsCompatible);
        Assert.Equal(
            ["binding", "storage", "category", "read_only"],
            result.Issues.Select(issue => issue.Code));
        Assert.All(result.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message)));
    }

    [Fact]
    public void FindCompatible_ReturnsOnlyItemsThatMeetTheFullRequirement()
    {
        ParameterCatalogItem compatible = CreateItem(20, ParameterStorageKind.String, writable: true);
        ParameterCatalogItem readOnly = CreateItem(21, ParameterStorageKind.String, writable: false);
        ParameterCatalogItem numeric = CreateItem(22, ParameterStorageKind.Double, writable: true);
        ParameterCatalog catalog = new([readOnly, numeric, compatible]);
        ParameterCatalogRequirement requirement = new(
            "Параметр записи",
            ParameterBindingKind.Instance,
            [ParameterStorageKind.String],
            [Rooms],
            requireWritable: true);

        IReadOnlyList<ParameterCatalogItem> result = new ParameterCatalogMatcher()
            .FindCompatible(catalog, requirement);

        ParameterCatalogItem selected = Assert.Single(result);
        Assert.Equal(compatible.Reference.StableKey, selected.Reference.StableKey);
    }

    private static ParameterCatalogItem CreateItem(
        long definitionId,
        ParameterStorageKind storageKind,
        bool writable)
    {
        return new ParameterCatalogItem(
            ParameterReference.Project(
                $"Параметр {definitionId}",
                definitionId,
                ParameterBindingKind.Instance,
                storageKind),
            [Rooms.Id],
            sampleCount: 1,
            writableSampleCount: writable ? 1 : 0,
            readOnlySampleCount: writable ? 0 : 1);
    }
}
