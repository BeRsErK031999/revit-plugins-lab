using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleParameterOptionServiceTests
{
    private static readonly FinishScheduleParameterCategories Categories = new(
        new ParameterCategoryReference(1, "Помещения"),
        new ParameterCategoryReference(2, "Стены"),
        new ParameterCategoryReference(3, "Перекрытия"),
        new ParameterCategoryReference(4, "Потолки"));

    [Fact]
    public void GetDescriptionOptions_RequiresEveryEnabledPhysicalCategory()
    {
        ParameterCatalogItem wallsOnly = CreateItem(
            101,
            "Описание стен",
            ParameterBindingKind.Type,
            ParameterStorageKind.String,
            [Categories.Walls.Id]);
        ParameterCatalogItem allCategories = CreateItem(
            102,
            "Общее описание",
            ParameterBindingKind.Type,
            ParameterStorageKind.String,
            [Categories.Walls.Id, Categories.Floors.Id, Categories.Ceilings.Id]);
        FinishScheduleParameterOptionService service = CreateService();
        ParameterCatalog catalog = new([wallsOnly, allCategories]);

        IReadOnlyList<FinishScheduleParameterOption> walls = service.GetDescriptionOptions(
            catalog,
            Categories,
            includeWalls: true,
            includeFloors: false,
            includeCeilings: false);
        IReadOnlyList<FinishScheduleParameterOption> all = service.GetDescriptionOptions(
            catalog,
            Categories,
            includeWalls: true,
            includeFloors: true,
            includeCeilings: true);

        Assert.Equal(2, walls.Count);
        Assert.Single(all);
        Assert.Equal(allCategories.Reference.StableKey, all[0].Reference.StableKey);
    }

    [Fact]
    public void GetOwnershipOptions_AcceptsActualCeilingCategoryParameter()
    {
        ParameterCatalogItem ceilingParameter = CreateItem(
            151,
            "Потолки • Номер помещения",
            ParameterBindingKind.Instance,
            ParameterStorageKind.String,
            [Categories.Ceilings.Id]);

        FinishScheduleParameterOption option = Assert.Single(
            CreateService().GetOwnershipOptions(
                new ParameterCatalog([ceilingParameter]),
                Categories.Ceilings));

        Assert.Equal(ceilingParameter.Reference.StableKey, option.Reference.StableKey);
    }

    [Fact]
    public void GetRoomOutputOptions_ExcludesReadOnlyAndNonTextParameters()
    {
        ParameterCatalogItem writableText = CreateItem(
            201,
            "Выход",
            ParameterBindingKind.Instance,
            ParameterStorageKind.String,
            [Categories.Rooms.Id]);
        ParameterCatalogItem readOnlyText = CreateItem(
            202,
            "Только чтение",
            ParameterBindingKind.Instance,
            ParameterStorageKind.String,
            [Categories.Rooms.Id],
            writable: false);
        ParameterCatalogItem numeric = CreateItem(
            203,
            "Число",
            ParameterBindingKind.Instance,
            ParameterStorageKind.Double,
            [Categories.Rooms.Id]);

        IReadOnlyList<FinishScheduleParameterOption> result = CreateService().GetRoomOutputOptions(
            new ParameterCatalog([readOnlyText, numeric, writableText]),
            Categories);

        FinishScheduleParameterOption option = Assert.Single(result);
        Assert.Equal(writableText.Reference.StableKey, option.Reference.StableKey);
        Assert.Contains("проектный", option.DisplayName, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void GetSectionOptions_AllowsReadableNumericParameter()
    {
        ParameterCatalogItem section = CreateItem(
            301,
            "Корпус",
            ParameterBindingKind.Instance,
            ParameterStorageKind.Integer,
            [Categories.Rooms.Id],
            writable: false);

        IReadOnlyList<FinishScheduleParameterOption> result = CreateService().GetSectionOptions(
            new ParameterCatalog([section]),
            Categories);

        Assert.Single(result);
    }

    private static FinishScheduleParameterOptionService CreateService()
    {
        return new FinishScheduleParameterOptionService(new ParameterCatalogMatcher());
    }

    private static ParameterCatalogItem CreateItem(
        long definitionId,
        string name,
        ParameterBindingKind binding,
        ParameterStorageKind storage,
        IEnumerable<long> categoryIds,
        bool writable = true)
    {
        return new ParameterCatalogItem(
            ParameterReference.Project(name, definitionId, binding, storage),
            categoryIds,
            sampleCount: 1,
            writableSampleCount: writable ? 1 : 0,
            readOnlySampleCount: writable ? 0 : 1);
    }
}
