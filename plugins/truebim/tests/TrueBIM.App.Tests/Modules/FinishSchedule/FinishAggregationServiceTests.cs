using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishAggregationServiceTests
{
    [Fact]
    public void SameDescriptionsWithDifferentAreas_CreateOneGroup()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101"), Room(2, "102")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "Paint"), Type(1012, "Paint")],
            [Occurrence(1, 11, 1.25), Occurrence(2, 12, 2.75)]);

        FinishAggregatedGroup group = Assert.Single(result.Groups);
        Assert.Equal([1L, 2L], group.RoomIds);
        Assert.Equal(4, Assert.Single(group.Walls.Items).AreaSquareMeters);
    }

    [Fact]
    public void DifferentDescriptions_CreateSeparateGroups()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101"), Room(2, "102")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "Paint"), Type(1012, "Tile")],
            [Occurrence(1, 11, 1), Occurrence(2, 12, 1)]);

        Assert.Equal(2, result.Groups.Count);
    }

    [Fact]
    public void DisabledCategory_DoesNotAffectGroupKeyOrOutput()
    {
        FinishAggregationResult result = Build(
            Settings(floors: false),
            [Room(1, "101"), Room(2, "102")],
            [
                Element(11, 1011),
                Element(12, 1012),
                Element(21, 1021, FinishPreviewCategory.Floors),
                Element(22, 1022, FinishPreviewCategory.Floors)
            ],
            [Type(1011, "Paint"), Type(1012, "Paint"), Type(1021, "Tile"), Type(1022, "Stone")],
            [
                Occurrence(1, 11, 1),
                Occurrence(2, 12, 1),
                Occurrence(1, 21, 1, FinishPreviewCategory.Floors),
                Occurrence(2, 22, 1, FinishPreviewCategory.Floors)
            ]);

        FinishAggregatedGroup group = Assert.Single(result.Groups);
        Assert.Null(group.Output.Floors);
    }

    [Fact]
    public void NoFinish_FormatsDescriptionAndAreaAsDash()
    {
        FinishAggregationResult result = Build(Settings(), [Room(1, "101")], [], [], []);

        FinishFormattedCategoryOutput output = Assert.Single(result.Groups).Output.Walls!;
        Assert.Equal(FinishAggregationFormatter.NoFinishDisplay, output.DescriptionText);
        Assert.Equal(FinishAggregationFormatter.NoFinishDisplay, output.AreaText);
    }

    [Fact]
    public void UnknownGeometry_NeverFormatsAsNoFinishDash()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101")],
            [],
            [],
            [],
            [GeometryWarning(1, FinishPreviewCategory.Walls)]);

        FinishFormattedCategoryOutput output = Assert.Single(result.Groups).Output.Walls!;
        Assert.Equal(FinishAggregationFormatter.UnknownDisplay, output.DescriptionText);
        Assert.Equal(FinishAggregationFormatter.UnknownDisplay, output.AreaText);
        Assert.NotEqual(FinishAggregationFormatter.NoFinishDisplay, output.AreaText);
    }

    [Fact]
    public void MultilineDescriptionAndArea_HaveMatchingNaturalOrderAndLineCount()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "Coat 10"), Type(1012, "Coat 2")],
            [Occurrence(1, 11, 10), Occurrence(1, 12, 2)]);

        FinishFormattedCategoryOutput output = Assert.Single(result.Groups).Output.Walls!;
        Assert.Equal(["Coat 2", string.Empty, "Coat 10"], Lines(output.DescriptionText));
        string[] areaLines = Lines(output.AreaText);
        Assert.Equal("2,00", areaLines[0]);
        Assert.True(string.IsNullOrWhiteSpace(areaLines[1]));
        Assert.Equal("10,00", areaLines[2]);
        Assert.Equal(Lines(output.DescriptionText).Length, Lines(output.AreaText).Length);
    }

    [Fact]
    public void LongDescriptions_CenterEachAreaInsideItsOwnVisualBlock()
    {
        FinishScheduleSettings settings = Settings() with
        {
            ColumnWidths = new FinishScheduleColumnWidths(40, 30, 25)
        };
        FinishAggregationResult result = Build(
            settings,
            [Room(1, "101")],
            [Element(11, 1011), Element(12, 1012), Element(13, 1013)],
            [
                Type(1011, "01 Первый многослойный вариант отделки с длинным подробным описанием материалов"),
                Type(1012, "02 Второй многослойный вариант отделки с другим длинным подробным описанием материалов"),
                Type(1013, "03 Третий многослойный вариант отделки с отдельным длинным подробным описанием материалов")
            ],
            [
                Occurrence(1, 11, 19.81),
                Occurrence(1, 12, 99.22),
                Occurrence(1, 13, 33.33)
            ]);

        FinishFormattedCategoryOutput output = Assert.Single(result.Groups).Output.Walls!;
        string[] descriptionLines = Lines(output.DescriptionText);
        string[] areaLines = Lines(output.AreaText);
        Assert.Equal(descriptionLines.Length, areaLines.Length);

        (int Start, int End)[] blocks = DescriptionBlocks(descriptionLines);
        Assert.Equal(3, blocks.Length);
        int[] areaRows = areaLines
            .Select((line, index) => (Line: line.Trim(), Index: index))
            .Where(item => item.Line is "19,81" or "99,22" or "33,33")
            .Select(item => item.Index)
            .ToArray();
        Assert.Equal(
            blocks.Select(block => block.Start + (block.End - block.Start) / 2),
            areaRows);
        Assert.All(
            blocks,
            block => Assert.True(block.End - block.Start >= 2));
    }

    [Fact]
    public void DiagnosticCandidateWarning_DoesNotAppendUnknownMarker()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101")],
            [Element(11, 1011)],
            [Type(1011, "Paint")],
            [Occurrence(1, 11, 2.5)],
            [
                new FinishGeometryWarning(
                    FinishGeometryWarningCode.BooleanIntersectionFailed,
                    "Speculative fallback candidate could not be intersected.",
                    RoomId: 1,
                    ElementId: 99,
                    Category: FinishPreviewCategory.Walls)
            ]);

        FinishFormattedCategoryOutput output = Assert.Single(result.Groups).Output.Walls!;
        Assert.Equal("Paint", output.DescriptionText);
        Assert.Equal("2,50", output.AreaText);
    }

    [Fact]
    public void DuplicateDescriptionWithinRoom_IsSummedBeforeGrouping()
    {
        RoomFinishSnapshotBuildResult snapshots = BuildSnapshots(
            Settings(),
            [Room(1, "101")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "Paint"), Type(1012, "paint")],
            [Occurrence(1, 11, 1.2), Occurrence(1, 12, 2.3)]);

        RoomFinishItem item = Assert.Single(Assert.Single(snapshots.Rooms).Walls.Items);
        Assert.Equal(3.5, item.AreaSquareMeters, 10);
    }

    [Fact]
    public void DuplicateDescriptionAcrossRooms_IsSummedInGroup()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101"), Room(2, "102")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "Paint"), Type(1012, "paint")],
            [Occurrence(1, 11, 1.2), Occurrence(2, 12, 2.3)]);

        FinishAggregatedItem item = Assert.Single(Assert.Single(result.Groups).Walls.Items);
        Assert.Equal(3.5, item.AreaSquareMeters, 10);
    }

    [Fact]
    public void AreaValues_AreExcludedFromGroupKey()
    {
        RoomFinishSnapshotBuildResult snapshots = BuildSnapshots(
            Settings(),
            [Room(1, "101"), Room(2, "102")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "Paint"), Type(1012, "Paint")],
            [Occurrence(1, 11, 1), Occurrence(2, 12, 999)]);
        FinishGroupKeyBuilder builder = new();

        Assert.Equal(
            builder.Create(snapshots.Rooms[0]),
            builder.Create(snapshots.Rooms[1]));
    }

    [Fact]
    public void RoomIdentifiers_AreSortedNaturally()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(10, "10"), Room(2, "2"), Room(1, "1")],
            [],
            [],
            []);

        Assert.Equal("1, 2, 10", Assert.Single(result.Groups).Output.RoomList);
    }

    [Fact]
    public void WhitespaceNewlinesAndCase_AreNormalizedForComparison()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101"), Room(2, "102")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "  PAINT\r\n  white "), Type(1012, "paint white")],
            [Occurrence(1, 11, 1), Occurrence(2, 12, 1)]);

        Assert.Single(result.Groups);
    }

    [Fact]
    public void InputOrder_DoesNotChangeKeysOrFormattedOutput()
    {
        FinishScheduleSettings settings = Settings();
        FinishRoomCandidateSnapshot[] rooms = [Room(1, "1"), Room(2, "2")];
        FinishClassifiedElement[] elements = [Element(11, 1011), Element(12, 1012)];
        FinishTypeSnapshot[] types = [Type(1011, "Paint"), Type(1012, "Paint")];
        FinishOccurrence[] occurrences = [Occurrence(1, 11, 1.25), Occurrence(2, 12, 2.75)];

        FinishAggregationResult first = Build(settings, rooms, elements, types, occurrences);
        FinishAggregationResult second = Build(
            settings,
            rooms.Reverse(),
            elements.Reverse(),
            types.Reverse(),
            occurrences.Reverse());

        Assert.Equal(first.Groups.Select(group => group.Key), second.Groups.Select(group => group.Key));
        Assert.Equal(first.Groups.Select(group => group.Output), second.Groups.Select(group => group.Output));
    }

    [Fact]
    public void NumericArea_IsRoundedOnlyByFormatter()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101"), Room(2, "102")],
            [Element(11, 1011), Element(12, 1012)],
            [Type(1011, "Paint"), Type(1012, "Paint")],
            [Occurrence(1, 11, 1.234), Occurrence(2, 12, 2.345)]);

        FinishAggregatedGroup group = Assert.Single(result.Groups);
        Assert.Equal(3.579, Assert.Single(group.Walls.Items).AreaSquareMeters, 10);
        Assert.Equal("3,58", group.Output.Walls!.AreaText);
    }

    [Fact]
    public void EmptyDescription_UsesPlaceholderAndEmitsWarning()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101")],
            [Element(11, 1011)],
            [Type(1011, " \r\n ")],
            [Occurrence(1, 11, 1)]);

        FinishAggregatedGroup group = Assert.Single(result.Groups);
        Assert.Equal(
            FinishDescriptionNormalizer.MissingDescriptionDisplay,
            Assert.Single(group.Walls.Items).Description.DisplayValue);
        Assert.Contains(result.Warnings, warning => warning.Code == FinishAggregationWarningCode.MissingDescription);
    }

    [Fact]
    public void RoomsInSameGroup_ReuseIdenticalOutputInstance()
    {
        FinishAggregationResult result = Build(
            Settings(),
            [Room(1, "101"), Room(2, "102")],
            [],
            [],
            []);

        Assert.Same(result.RoomOutputs[1], result.RoomOutputs[2]);
        Assert.Same(Assert.Single(result.Groups).Output, result.RoomOutputs[1]);
    }

    private static FinishAggregationResult Build(
        FinishScheduleSettings settings,
        IEnumerable<FinishRoomCandidateSnapshot> rooms,
        IEnumerable<FinishClassifiedElement> elements,
        IEnumerable<FinishTypeSnapshot> types,
        IEnumerable<FinishOccurrence> occurrences,
        IEnumerable<FinishGeometryWarning>? warnings = null)
    {
        RoomFinishSnapshotBuildResult snapshots = BuildSnapshots(
            settings,
            rooms,
            elements,
            types,
            occurrences,
            warnings);
        return new FinishAggregationService(
            new FinishGroupKeyBuilder(),
            new FinishAggregationFormatter(
                settings.EffectiveColumnWidths.DescriptionMillimeters)).Aggregate(snapshots);
    }

    private static RoomFinishSnapshotBuildResult BuildSnapshots(
        FinishScheduleSettings settings,
        IEnumerable<FinishRoomCandidateSnapshot> rooms,
        IEnumerable<FinishClassifiedElement> elements,
        IEnumerable<FinishTypeSnapshot> types,
        IEnumerable<FinishOccurrence> occurrences,
        IEnumerable<FinishGeometryWarning>? warnings = null)
    {
        FinishClassifiedElement[] elementArray = elements.ToArray();
        return new RoomFinishSnapshotBuilder(new FinishDescriptionNormalizer()).Build(
            new RoomFinishSnapshotRequest(
                settings,
                rooms,
                elementArray,
                types.ToDictionary(type => type.TypeId),
                new FinishQuantityResult(occurrences, warnings ?? [])));
    }

    private static FinishScheduleSettings Settings(bool floors = false, bool ceilings = false)
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        return defaults with
        {
            DescriptionParameter = ParameterReference.Project(
                "Description",
                1,
                ParameterBindingKind.Type,
                ParameterStorageKind.String),
            Floors = defaults.Floors with { IsEnabled = floors },
            Ceilings = defaults.Ceilings with { IsEnabled = ceilings }
        };
    }

    private static FinishRoomCandidateSnapshot Room(long id, string number)
    {
        return new FinishRoomCandidateSnapshot(
            id,
            1,
            20,
            true,
            new AxisAlignedBox3D(0, 0, 0, 10, 10, 3),
            number: number);
    }

    private static FinishClassifiedElement Element(
        long id,
        long typeId,
        FinishPreviewCategory category = FinishPreviewCategory.Walls)
    {
        FinishPhysicalCategory physicalCategory = category == FinishPreviewCategory.Walls
            ? FinishPhysicalCategory.Wall
            : FinishPhysicalCategory.Floor;
        return new FinishClassifiedElement(
            new FinishElementCandidateSnapshot(
                id,
                typeId,
                physicalCategory,
                new AxisAlignedBox3D(0, 0, 0, 10, 10, 3)),
            category);
    }

    private static FinishTypeSnapshot Type(long typeId, string description)
    {
        return new FinishTypeSnapshot(
            typeId,
            null,
            false,
            new FinishParameterValueSnapshot(description, description),
            true);
    }

    private static FinishOccurrence Occurrence(
        long roomId,
        long elementId,
        double area,
        FinishPreviewCategory category = FinishPreviewCategory.Walls)
    {
        return new FinishOccurrence(
            roomId,
            elementId,
            category,
            area,
            FinishQuantityMethod.RoomBoundarySubface);
    }

    private static FinishGeometryWarning GeometryWarning(
        long roomId,
        FinishPreviewCategory category)
    {
        return new FinishGeometryWarning(
            FinishGeometryWarningCode.ProjectedAreaUnavailable,
            "Geometry is unresolved.",
            RoomId: roomId,
            Category: category);
    }

    private static string[] Lines(string value)
    {
        return value.Split([Environment.NewLine], StringSplitOptions.None);
    }

    private static (int Start, int End)[] DescriptionBlocks(string[] lines)
    {
        List<(int Start, int End)> blocks = [];
        int start = 0;
        for (int index = 0; index <= lines.Length; index++)
        {
            if (index < lines.Length && lines[index].Length > 0)
            {
                continue;
            }

            if (index > start)
            {
                blocks.Add((start, index - 1));
            }

            start = index + 1;
        }

        return blocks.ToArray();
    }
}
