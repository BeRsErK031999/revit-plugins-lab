using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishClassificationServiceTests
{
    [Fact]
    public void Classify_UsesCachedTypesAndPhysicalCategories()
    {
        FinishElementCollection collection = Collection(
        [
            Element(1, 101, FinishPhysicalCategory.Wall),
            Element(2, 102, FinishPhysicalCategory.Floor),
            Element(3, 103, FinishPhysicalCategory.Floor)
        ],
        [
            new FinishTypeSnapshot(101, "внутренняя отделка", true),
            new FinishTypeSnapshot(102, "Пол", true),
            new FinishTypeSnapshot(103, "Потолки", true)
        ]);

        FinishClassificationResult result = new FinishClassificationService().Classify(
            collection,
            FinishScheduleSettings.CreateDefault());

        Assert.Equal(
            [FinishPreviewCategory.Walls, FinishPreviewCategory.Floors, FinishPreviewCategory.Ceilings],
            result.Elements.Select(element => element.Category));
        Assert.Empty(result.SkippedElements);
    }

    [Fact]
    public void Classify_ExplainsMissingAndMismatchedClassification()
    {
        FinishElementCollection collection = Collection(
        [
            Element(1, 101, FinishPhysicalCategory.Wall),
            Element(2, 102, FinishPhysicalCategory.Wall),
            Element(3, 103, FinishPhysicalCategory.Wall),
            Element(4, 104, FinishPhysicalCategory.Wall)
        ],
        [
            new FinishTypeSnapshot(102, null, false),
            new FinishTypeSnapshot(103, string.Empty, true),
            new FinishTypeSnapshot(104, "Фасад", true)
        ]);

        FinishClassificationResult result = new FinishClassificationService().Classify(
            collection,
            FinishScheduleSettings.CreateDefault());

        Assert.Empty(result.Elements);
        Assert.Equal(
            [
                FinishClassificationSkipReason.MissingType,
                FinishClassificationSkipReason.MissingClassificationParameter,
                FinishClassificationSkipReason.EmptyClassificationValue,
                FinishClassificationSkipReason.ValueDoesNotMatch
            ],
            result.SkippedElements.Select(element => element.Reason));
    }

    [Fact]
    public void Classify_RejectsAmbiguousFloorAndCeilingValue()
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        FinishScheduleSettings settings = defaults with
        {
            Ceilings = defaults.Ceilings with { ClassificationValue = "Пол" }
        };
        FinishElementCollection collection = Collection(
            [Element(1, 101, FinishPhysicalCategory.Floor)],
            [new FinishTypeSnapshot(101, "Пол", true)]);

        FinishClassificationResult result = new FinishClassificationService().Classify(
            collection,
            settings);

        Assert.Empty(result.Elements);
        Assert.Equal(
            FinishClassificationSkipReason.AmbiguousFloorClassification,
            Assert.Single(result.SkippedElements).Reason);
    }

    private static FinishElementCollection Collection(
        IEnumerable<FinishElementCandidateSnapshot> elements,
        IEnumerable<FinishTypeSnapshot> types)
    {
        FinishElementCandidateSnapshot[] all = elements.ToArray();
        return new FinishElementCollection(
            [],
            all.Where(element => element.PhysicalCategory == FinishPhysicalCategory.Wall),
            all.Where(element => element.PhysicalCategory == FinishPhysicalCategory.Floor),
            types);
    }

    private static FinishElementCandidateSnapshot Element(
        long id,
        long typeId,
        FinishPhysicalCategory category)
    {
        return new FinishElementCandidateSnapshot(
            id,
            typeId,
            category,
            new AxisAlignedBox3D(0, 0, 0, 1, 1, 1));
    }
}
