using TrueBIM.App.Modules.Lintels;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelsModuleStatusTests
{
    [Fact]
    public void Create_ReportsAtomicAssemblyCreationForOpenDocument()
    {
        LintelsModuleStatus status = LintelsModuleStatus.Create("Lintels sample");

        Assert.Equal("Lintels sample", status.DocumentName);
        Assert.True(status.CanModifyModel);
        Assert.Contains(status.ReadyCapabilities, item => item.Contains("КР", StringComparison.Ordinal));
        Assert.Contains(status.ReadyCapabilities, item => item.Contains("Preflight", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(status.ReadyCapabilities, item => item.Contains("атомарно", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(status.PendingCapabilities, item => item.Contains("1:10", StringComparison.Ordinal));
        Assert.Contains("изменений модели", status.ToDialogText(), StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void Create_ExplainsWhenDocumentIsNotOpen()
    {
        LintelsModuleStatus status = LintelsModuleStatus.Create(null);

        Assert.Equal("Документ Revit не открыт", status.DocumentName);
        Assert.False(status.CanModifyModel);
    }
}
