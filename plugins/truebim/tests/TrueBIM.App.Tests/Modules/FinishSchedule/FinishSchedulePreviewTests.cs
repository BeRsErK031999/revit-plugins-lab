using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishSchedulePreviewTests
{
    [Fact]
    public void Build_CountsScopeClassificationAndPotentialPairs()
    {
        FinishElementCollection collection = new(
        [
            Room(1, new AxisAlignedBox3D(0, 0, 0, 10, 10, 3)),
            Room(2, new AxisAlignedBox3D(20, 0, 0, 30, 10, 3)),
            new FinishRoomCandidateSnapshot(3, 10, 0, true, null)
        ],
        [
            Element(101, 201, FinishPhysicalCategory.Wall, new AxisAlignedBox3D(1, 1, 0, 2, 2, 3)),
            Element(102, 202, FinishPhysicalCategory.Wall, null)
        ],
        [
            Element(103, 203, FinishPhysicalCategory.Floor, new AxisAlignedBox3D(21, 1, 0, 22, 2, 1)),
            Element(104, 204, FinishPhysicalCategory.Floor, new AxisAlignedBox3D(50, 1, 0, 52, 2, 1))
        ],
        [
            new FinishTypeSnapshot(201, "Внутренняя отделка", true),
            new FinishTypeSnapshot(202, "Внутренняя отделка", true),
            new FinishTypeSnapshot(203, "Пол", true),
            new FinishTypeSnapshot(204, "Не отделка", true)
        ]);
        FinishSchedulePreviewBuilder builder = new(
            new RoomScopeService(),
            new FinishClassificationService());

        FinishSchedulePreviewBuild build = builder.BuildDetailed(
            collection,
            FinishScheduleSettings.CreateDefault());
        FinishSchedulePreviewResult result = build.Preview;

        Assert.Equal(2, result.RoomScope.SelectedRooms.Count);
        Assert.Single(result.RoomScope.InvalidRooms);
        Assert.Equal(new FinishPreviewCategoryCounts(2, 2, 1), result.Walls);
        Assert.Equal(new FinishPreviewCategoryCounts(2, 1, 1), result.Floors);
        Assert.Equal(new FinishPreviewCategoryCounts(2, 0, 0), result.Ceilings);
        Assert.Equal(2, result.Index.IndexedElements);
        Assert.Equal(1, result.Index.ElementsWithoutBounds);
        Assert.Equal(2, result.Index.PotentialRoomElementPairs);
        Assert.Equal([101L, 103L], build.InScopeElements.Select(element => element.Element.ElementId));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void PreviewValidator_DoesNotRequireOutputParameters()
    {
        FinishScheduleValidationResult result = new FinishSchedulePreviewValidator().Validate(
            FinishScheduleSettings.CreateDefault());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Build_IncludesNearbyFloorAndCeilingOutsideStrictRoomHeight()
    {
        FinishElementCollection collection = new(
            [Room(1, new AxisAlignedBox3D(0, 0, 0, 10, 10, 10))],
            [],
            [Element(201, 301, FinishPhysicalCategory.Floor, new AxisAlignedBox3D(1, 1, -0.04, 9, 9, -0.01))],
            [
                Element(202, 302, FinishPhysicalCategory.Ceiling, new AxisAlignedBox3D(1, 1, 12, 9, 9, 13)),
                Element(203, 302, FinishPhysicalCategory.Ceiling, new AxisAlignedBox3D(1, 1, 20, 9, 9, 21))
            ],
            [
                new FinishTypeSnapshot(301, "Пол", true),
                new FinishTypeSnapshot(302, "Потолки", true)
            ]);
        FinishSchedulePreviewBuild build = new FinishSchedulePreviewBuilder(
            new RoomScopeService(),
            new FinishClassificationService()).BuildDetailed(
                collection,
                FinishScheduleSettings.CreateDefault());

        Assert.Equal(new FinishPreviewCategoryCounts(1, 1, 1), build.Preview.Floors);
        Assert.Equal(new FinishPreviewCategoryCounts(3, 2, 1), build.Preview.Ceilings);
        Assert.Equal(2, build.Preview.Index.PotentialRoomElementPairs);
        Assert.Equal([201L, 202L], build.InScopeElements.Select(element => element.Element.ElementId));
    }

    [Fact]
    public void PreviewValidator_RequiresEnabledCategoryClassificationAndScopeValue()
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        FinishScheduleSettings settings = defaults with
        {
            Walls = defaults.Walls with { IsEnabled = false },
            Floors = defaults.Floors with { IsEnabled = false },
            Ceilings = defaults.Ceilings with { ClassificationValue = string.Empty },
            Scope = new ReportScopeSettings(ReportScopeKind.Section, null, null, string.Empty)
        };

        FinishScheduleValidationResult result = new FinishSchedulePreviewValidator().Validate(settings);

        Assert.Contains(result.Issues, issue => issue.Code == "ceilings.classification.empty");
        Assert.Contains(result.Issues, issue => issue.Code == "scope.section_parameter.missing");
        Assert.Contains(result.Issues, issue => issue.Code == "scope.section_value.empty");
    }

    [Fact]
    public void Build_LargeCandidateSet_ReportsOnlySpatialIndexPairs()
    {
        FinishElementCandidateSnapshot[] walls = Enumerable.Range(0, 1000)
            .Select(index => Element(
                1000 + index,
                500,
                FinishPhysicalCategory.Wall,
                index == 0
                    ? new AxisAlignedBox3D(1, 1, 0, 2, 2, 3)
                    : new AxisAlignedBox3D(100 + index * 10, 0, 0, 101 + index * 10, 1, 3)))
            .ToArray();
        FinishElementCollection collection = new(
            [Room(1, new AxisAlignedBox3D(0, 0, 0, 10, 10, 3))],
            walls,
            [],
            [new FinishTypeSnapshot(500, "Внутренняя отделка", true)]);
        FinishSchedulePreviewBuilder builder = new(
            new RoomScopeService(),
            new FinishClassificationService());

        FinishSchedulePreviewResult result = builder.Build(
            collection,
            FinishScheduleSettings.CreateDefault());

        Assert.Equal(1000, result.Walls.Classified);
        Assert.Equal(1, result.Walls.InScope);
        Assert.Equal(1, result.Index.PotentialRoomElementPairs);
    }

    [Fact]
    public void Build_NormalClassificationMismatches_AreNotWarnings()
    {
        FinishElementCollection collection = new(
            [Room(1, new AxisAlignedBox3D(0, 0, 0, 10, 10, 3))],
            [Element(101, 201, FinishPhysicalCategory.Wall, new AxisAlignedBox3D(1, 1, 0, 2, 2, 3))],
            [],
            [new FinishTypeSnapshot(201, "Не отделка", true)]);
        FinishSchedulePreviewBuilder builder = new(
            new RoomScopeService(),
            new FinishClassificationService());

        FinishSchedulePreviewResult result = builder.Build(
            collection,
            FinishScheduleSettings.CreateDefault());

        Assert.Empty(result.Warnings);
        Assert.Equal(0, result.Walls.Classified);
    }

    private static FinishRoomCandidateSnapshot Room(long id, AxisAlignedBox3D bounds)
    {
        return new FinishRoomCandidateSnapshot(id, 10, 20, true, bounds);
    }

    private static FinishElementCandidateSnapshot Element(
        long id,
        long typeId,
        FinishPhysicalCategory category,
        AxisAlignedBox3D? bounds)
    {
        return new FinishElementCandidateSnapshot(id, typeId, category, bounds);
    }
}
