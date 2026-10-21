using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishMissingDescriptionValidatorTests
{
    [Fact]
    public void ChecksOnlyPlacedFinishTypes_DeduplicatesAndIgnoresUnusedOrStructuralTypes()
    {
        FinishElementCollection collection = new([], [Element(1, 10), Element(2, 10), Element(3, 30), Element(4, 40)], [],
        [
            new FinishTypeSnapshot(10, "Внутренняя отделка", true, new(" \r\n", " "), true),
            new FinishTypeSnapshot(20, "Внутренняя отделка", true), // Unused type.
            new FinishTypeSnapshot(30, "Несущая стена", true),
            new FinishTypeSnapshot(40, "Внутренняя отделка", true, new("", "Окраска"), true)
        ]);
        Assert.Equal(new long[] { 10 }, FinishMissingDescriptionValidator.FindTypeIds(collection, FinishScheduleSettings.CreateDefault()));
    }

    [Fact]
    public void DisabledCategoryIsExcludedAndMissingParameterIsReported()
    {
        FinishElementCollection collection = new([], [Element(1, 10)], [],
            [new FinishTypeSnapshot(10, "Внутренняя отделка", true)]);
        FinishScheduleSettings settings = FinishScheduleSettings.CreateDefault();
        Assert.Equal(new long[] { 10 }, FinishMissingDescriptionValidator.FindTypeIds(collection, settings));
        Assert.Empty(FinishMissingDescriptionValidator.FindTypeIds(collection,
            settings with { Walls = settings.Walls with { IsEnabled = false } }));
    }

    private static FinishElementCandidateSnapshot Element(long id, long type) => new(id, type, FinishPhysicalCategory.Wall, null);
}
