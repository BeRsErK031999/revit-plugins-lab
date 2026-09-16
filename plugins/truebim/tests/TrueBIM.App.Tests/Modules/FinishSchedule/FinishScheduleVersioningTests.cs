using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleVersioningTests
{
    [Fact]
    public void Naming_UsesFreeBaseNameAndSkipsOccupiedNamesRegardlessOfCase()
    {
        Assert.Equal("Отделка", FinishScheduleVersionNameService.CreateUniqueName("Отделка", []));
        Assert.Equal("Отделка (3)", FinishScheduleVersionNameService.CreateUniqueName("Отделка",
            ["ОТДЕЛКА", "Отделка (2)", "Другая ведомость"]));
    }

    [Fact]
    public void Planner_TreatsRevitLineEndingConversionAsUnchanged()
    {
        FinishWritePlan plan = Plan("01 Coat\r\n02 Tile", "01 Coat\n02 Tile");
        Assert.Empty(plan.Changes);
        Assert.Equal(1, plan.UnchangedCount);
    }

    [Fact]
    public void Planner_SkipsOuterPaddingDiscardedByRevitAndPreservesInternalAlignment()
    {
        Assert.Empty(Plan("139,79", "\u00a0\r\n139,79\r\n\u00a0").Changes);
        Assert.Empty(Plan("\u00a0\n139,79\n\u00a0", "\u00a0\r\n139,79\r\n\u00a0").Changes);
        Assert.Single(Plan("139,79\n20,00", "139,79\n\u00a0\n20,00").Changes);
    }

    [Fact]
    public void Confirmation_ExplainsAlignmentWithoutPretendingTheNumberChanged()
    {
        FinishScheduleWriteConfirmation result = new FinishScheduleWriteConfirmationBuilder()
            .Build(Preview(Plan("139,79\n20,00", "139,79\n\u00a0\n20,00")));
        Assert.False(result.ReplacesExistingValues);
        Assert.Contains("Только оформление текста: 1", result.Message);
        Assert.Contains("сами числа и текст сохранятся", result.Message);
        Assert.DoesNotContain("Примеры изменения значений", result.Message);
        Assert.DoesNotContain("Будут заменены заполненные", result.Message);
    }

    [Fact]
    public void Confirmation_ShowsRealReplacementAndReadableRoomLabel()
    {
        FinishScheduleWriteConfirmation result = new FinishScheduleWriteConfirmationBuilder()
            .Build(Preview(Plan("139,79", "140,00")));
        Assert.True(result.ReplacesExistingValues);
        Assert.Contains("Помещение 101 «Кабинет»", result.Message);
        Assert.Contains("«139,79» → «140,00»", result.Message);
        Assert.Contains("Ручные правки", result.Message);
        Assert.Contains("Прежние версии TrueBIM сохранят свои значения и размещение", result.Message);
    }

    [Fact]
    public void Confirmation_ClearingIsExplicitAndWarnsAboutExistingValue()
    {
        FinishScheduleWriteConfirmation result = new FinishScheduleWriteConfirmationBuilder().Build(Preview(Plan("139,79", "")));
        Assert.True(result.ReplacesExistingValues);
        Assert.Contains("Будут очищены значения: 1", result.Message);
        Assert.Contains("«139,79» → «пусто»", result.Message);
    }

    [Fact]
    public void UnchangedParameters_StillCreateNewSnapshotWithoutParameterWrites()
    {
        FinishScheduleWritePreview preview = Preview(Plan("139,79", "139,79"), [55]);
        FinishScheduleWriteConfirmation result = new FinishScheduleWriteConfirmationBuilder().Build(preview);
        Assert.True(preview.RequiresTransaction);
        Assert.Equal(0, preview.TotalChangeCount);
        Assert.Contains("Параметры не будут перезаписаны", result.Message);
        Assert.Contains("новая версия «Отделка (2)»", result.Message);
        Assert.Contains("Ведомостей из прежней версии плагина: 1", result.Message);
    }

    private static readonly ParameterReference Parameter = ParameterReference.BuiltIn(
        "Площадь", -1, ParameterBindingKind.Instance, ParameterStorageKind.String);

    private static FinishWritePlan Plan(string previous, string next) => new FinishParameterChangePlanner().Create(1,
        [new FinishParameterWriteCandidate(new FinishParameterTargetValue(10, Parameter, "Площадь стен", next, true), previous)]);

    private static FinishScheduleWritePreview Preview(FinishWritePlan roomPlan, long[]? legacyIds = null)
    {
        FinishRoomSchedulePlan schedulePlan = new("Отделка (2)",
            [new FinishRoomScheduleColumn(Parameter, "Площадь", 25, FinishRoomScheduleColumnKind.Area)],
            FinishRoomScheduleScopeFilter.EntireProject(), "version-test", [Parameter.StableKey]);
        return new FinishScheduleWritePreview(1, 1, roomPlan, FinishWritePlan.Empty(), [],
            new FinishRoomSchedulePreflight(schedulePlan, FinishRoomScheduleAction.Create, null, [], legacyIds),
            elementLabels: new Dictionary<long, string> { [10] = "Помещение 101 «Кабинет»" });
    }
}
