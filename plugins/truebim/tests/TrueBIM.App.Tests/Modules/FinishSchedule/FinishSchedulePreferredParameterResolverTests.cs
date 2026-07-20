using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishSchedulePreferredParameterResolverTests
{
    private static readonly FinishScheduleParameterCategories Categories = new(
        new ParameterCategoryReference(1, "Помещения"),
        new ParameterCategoryReference(2, "Стены"),
        new ParameterCategoryReference(3, "Перекрытия"),
        new ParameterCategoryReference(4, "Потолки"));

    [Fact]
    public void Resolve_SelectsUniqueCompatiblePreferredParameters()
    {
        ParameterCatalog catalog = new(
        [
            CreateItem(
                100,
                FinishSchedulePreferredParameterNames.Description,
                ParameterBindingKind.Type,
                [Categories.Walls.Id, Categories.Floors.Id, Categories.Ceilings.Id]),
            CreateRoomOutput(101, FinishSchedulePreferredParameterNames.RoomListOutput),
            CreateRoomOutput(102, FinishSchedulePreferredParameterNames.WallsDescription),
            CreateRoomOutput(103, FinishSchedulePreferredParameterNames.WallsArea),
            CreateRoomOutput(104, FinishSchedulePreferredParameterNames.FloorsDescription),
            CreateRoomOutput(105, FinishSchedulePreferredParameterNames.FloorsArea),
            CreateRoomOutput(106, FinishSchedulePreferredParameterNames.CeilingsDescription),
            CreateRoomOutput(107, FinishSchedulePreferredParameterNames.CeilingsArea),
            CreateItem(
                108,
                FinishSchedulePreferredParameterNames.WallsOwnership,
                ParameterBindingKind.Instance,
                [Categories.Walls.Id]),
            CreateItem(
                109,
                FinishSchedulePreferredParameterNames.FloorsOwnership,
                ParameterBindingKind.Instance,
                [Categories.Floors.Id]),
            CreateItem(
                110,
                FinishSchedulePreferredParameterNames.CeilingsOwnership,
                ParameterBindingKind.Instance,
                [Categories.Ceilings.Id])
        ]);
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();

        FinishScheduleSettings resolved = CreateResolver().Resolve(
            defaults with { WriteOwnership = true },
            catalog,
            Categories);

        Assert.Equal(FinishSchedulePreferredParameterNames.Description, resolved.DescriptionParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.RoomListOutput, resolved.RoomListOutputParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.WallsDescription, resolved.Walls.OutputDescriptionParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.WallsArea, resolved.Walls.OutputAreaParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.FloorsDescription, resolved.Floors.OutputDescriptionParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.FloorsArea, resolved.Floors.OutputAreaParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.CeilingsDescription, resolved.Ceilings.OutputDescriptionParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.CeilingsArea, resolved.Ceilings.OutputAreaParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.WallsOwnership, resolved.Walls.OwnershipParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.FloorsOwnership, resolved.Floors.OwnershipParameter!.Name);
        Assert.Equal(FinishSchedulePreferredParameterNames.CeilingsOwnership, resolved.Ceilings.OwnershipParameter!.Name);
    }

    [Fact]
    public void Resolve_DoesNotGuessWhenPreferredNameIsAmbiguous()
    {
        ParameterCatalog catalog = new(
        [
            CreateRoomOutput(201, FinishSchedulePreferredParameterNames.RoomListOutput),
            CreateRoomOutput(202, FinishSchedulePreferredParameterNames.RoomListOutput)
        ]);

        FinishScheduleSettings resolved = CreateResolver().Resolve(
            FinishScheduleSettings.CreateDefault(),
            catalog,
            Categories);

        Assert.Null(resolved.RoomListOutputParameter);
    }

    [Fact]
    public void Resolve_PreservesExplicitExistingSelection()
    {
        ParameterCatalogItem selected = CreateRoomOutput(301, "Мой список помещений");
        ParameterCatalog catalog = new(
        [
            selected,
            CreateRoomOutput(302, FinishSchedulePreferredParameterNames.RoomListOutput)
        ]);
        FinishScheduleSettings settings = FinishScheduleSettings.CreateDefault() with
        {
            RoomListOutputParameter = selected.Reference
        };

        FinishScheduleSettings resolved = CreateResolver().Resolve(settings, catalog, Categories);

        Assert.Equal(selected.Reference.StableKey, resolved.RoomListOutputParameter!.StableKey);
    }

    [Fact]
    public void Resolve_RebindsPortableProjectSelectionByUniqueName()
    {
        ParameterCatalogItem currentModel = CreateRoomOutput(401, "Мой список помещений");
        FinishScheduleSettings imported = FinishScheduleSettings.CreateDefault() with
        {
            RoomListOutputParameter = ParameterReference.Project(
                "Мой список помещений",
                999,
                ParameterBindingKind.Instance,
                ParameterStorageKind.String)
        };

        FinishScheduleSettings resolved = CreateResolver().Resolve(
            imported,
            new ParameterCatalog([currentModel]),
            Categories);

        Assert.Equal(currentModel.Reference.StableKey, resolved.RoomListOutputParameter!.StableKey);
    }

    private static FinishSchedulePreferredParameterResolver CreateResolver()
    {
        ParameterCatalogMatcher matcher = new();
        return new FinishSchedulePreferredParameterResolver(
            new FinishScheduleParameterOptionService(matcher));
    }

    private static ParameterCatalogItem CreateRoomOutput(long definitionId, string name)
    {
        return CreateItem(definitionId, name, ParameterBindingKind.Instance, [Categories.Rooms.Id]);
    }

    private static ParameterCatalogItem CreateItem(
        long definitionId,
        string name,
        ParameterBindingKind bindingKind,
        IEnumerable<long> categoryIds)
    {
        ParameterReference reference = ParameterReference.Project(
            name,
            definitionId,
            bindingKind,
            ParameterStorageKind.String);
        return new ParameterCatalogItem(
            reference,
            categoryIds,
            sampleCount: 1,
            writableSampleCount: 1,
            readOnlySampleCount: 0);
    }
}
