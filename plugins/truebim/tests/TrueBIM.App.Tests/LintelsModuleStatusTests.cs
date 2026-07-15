using TrueBIM.App.Modules.Lintels;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelsModuleStatusTests
{
    [Fact]
    public void Create_ReturnsReadOnlyScaffoldStatus()
    {
        LintelsModuleStatus status = LintelsModuleStatus.Create("Lintels sample");

        Assert.Equal("Lintels sample", status.DocumentName);
        Assert.False(status.CanModifyModel);
        Assert.Contains(status.ReadyCapabilities, item => item.Contains("КР", StringComparison.Ordinal));
        Assert.Contains(status.PendingCapabilities, item => item.Contains("сборок", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains("изменений модели", status.ToDialogText(), StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void Create_ExplainsWhenDocumentIsNotOpen()
    {
        LintelsModuleStatus status = LintelsModuleStatus.Create(null);

        Assert.Equal("Документ Revit не открыт", status.DocumentName);
    }
}
