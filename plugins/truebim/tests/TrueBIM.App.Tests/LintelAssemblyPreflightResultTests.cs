using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelAssemblyPreflightResultTests
{
    [Fact]
    public void BuildSummary_ReportsEveryOutcomeAndReadOnlyContract()
    {
        LintelAssemblyPreflightResult result = new(
        [
            CreateItem(1, LintelAssemblyPreflightStatus.Ready),
            CreateItem(2, LintelAssemblyPreflightStatus.AlreadyExists),
            CreateItem(3, LintelAssemblyPreflightStatus.Blocked)
        ]);

        Assert.Equal(1, result.ReadyCount);
        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(1, result.BlockedCount);
        Assert.Contains("Готово к созданию: 1", result.BuildSummary(), StringComparison.CurrentCulture);
        Assert.Contains("не изменял модель Revit", result.BuildSummary(), StringComparison.CurrentCulture);
    }

    [Theory]
    [InlineData(LintelAssemblyPreflightStatus.Ready, "Готово")]
    [InlineData(LintelAssemblyPreflightStatus.AlreadyExists, "Уже существует")]
    [InlineData(LintelAssemblyPreflightStatus.Blocked, "Заблокировано")]
    public void StatusDisplay_UsesRussianWorkflowLabels(
        LintelAssemblyPreflightStatus status,
        string expected)
    {
        Assert.Equal(expected, CreateItem(1, status).StatusDisplay);
    }

    [Fact]
    public void MemberIdsDisplay_ListsPlannedRevitElements()
    {
        LintelAssemblyPreflightItem item = CreateItem(7, LintelAssemblyPreflightStatus.Ready);

        Assert.Equal("207", item.MemberIdsDisplay);
    }

    private static LintelAssemblyPreflightItem CreateItem(
        long typeId,
        LintelAssemblyPreflightStatus status)
    {
        return new LintelAssemblyPreflightItem(
            typeId,
            "Перемычки",
            $"ПР-{typeId}",
            $"TB_Перемычка_ПР-{typeId}_{typeId}",
            100 + typeId,
            [200 + typeId],
            status == LintelAssemblyPreflightStatus.Blocked ? null : -2000151,
            status,
            "Результат проверки");
    }
}
