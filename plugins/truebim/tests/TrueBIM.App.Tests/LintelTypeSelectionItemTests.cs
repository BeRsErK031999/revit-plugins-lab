using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using TrueBIM.App.Modules.Lintels.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelTypeSelectionItemTests
{
    [Fact]
    public void Constructor_LeavesReadyTypeUnselected()
    {
        LintelTypeSelectionItem item = new(CreateType());

        Assert.True(item.CanSelect);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void ExistingAssembly_IsShownAsReadyForViewFormatting()
    {
        LintelTypeDiagnostic type = CreateType();
        string assemblyName = LintelArtifactNameBuilder.Build(type).AssemblyName;
        LintelTypeSelectionItem item = new(type with { ExistingAssemblyName = assemblyName });

        Assert.Equal("Сборка уже есть", item.ReadyStatus);
        Assert.Contains(assemblyName, item.DiagnosticText, StringComparison.Ordinal);
        Assert.Contains("оформить", item.DiagnosticText, StringComparison.CurrentCultureIgnoreCase);
    }

    private static LintelTypeDiagnostic CreateType()
    {
        return new LintelTypeDiagnostic(
            100,
            "Перемычка сварная",
            "ПР-1",
            1,
            1,
            10,
            false,
            1,
            1,
            [11],
            []);
    }
}
