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
            status.PendingCapabilities,
            item => item.Contains("параметров", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(
            status.PendingCapabilities,
            item => item.Contains("геометр", StringComparison.CurrentCultureIgnoreCase));
    }

    [Fact]
    public void Create_ExplainsWhenDocumentIsNotOpen()
    {
        FinishScheduleModuleStatus status = FinishScheduleModuleStatus.Create(null);

        Assert.Equal("Документ Revit не открыт", status.DocumentName);
        Assert.False(status.HasActiveDocument);
    }
}
