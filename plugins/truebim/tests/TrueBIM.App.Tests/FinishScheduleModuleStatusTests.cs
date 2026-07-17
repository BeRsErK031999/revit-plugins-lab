using TrueBIM.App.Modules.FinishSchedule;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class FinishScheduleModuleStatusTests
{
    [Fact]
    public void Create_ReportsSafeScaffoldForOpenDocument()
    {
        FinishScheduleModuleStatus status = FinishScheduleModuleStatus.Create("Finish sample");

        Assert.Equal("Finish sample", status.DocumentName);
        Assert.True(status.HasActiveDocument);
        Assert.Contains(status.ReadyCapabilities, item => item.Contains("АР", StringComparison.Ordinal));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("не изменяет модель", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("GUID", StringComparison.Ordinal));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("восемь", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("finish-schedule", StringComparison.Ordinal));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("preview", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("геометр", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("агрегац", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("TransactionGroup", StringComparison.Ordinal));
        Assert.Contains(
            status.ReadyCapabilities,
            item => item.Contains("ownership", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            status.PendingCapabilities,
            item => item.Contains("спецификац", StringComparison.CurrentCultureIgnoreCase));
    }

    [Fact]
    public void Create_ExplainsWhenDocumentIsNotOpen()
    {
        FinishScheduleModuleStatus status = FinishScheduleModuleStatus.Create(null);

        Assert.Equal("Документ Revit не открыт", status.DocumentName);
        Assert.False(status.HasActiveDocument);
    }
}
