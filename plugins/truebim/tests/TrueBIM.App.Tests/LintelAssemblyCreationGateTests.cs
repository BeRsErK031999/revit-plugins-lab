using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelAssemblyCreationGateTests
{
    [Fact]
    public void CanStart_AllowsOneOrMoreSelectedTypes()
    {
        Assert.True(LintelAssemblyCreationGate.CanStart([100]));
        Assert.True(LintelAssemblyCreationGate.CanStart([100, 200]));
        Assert.False(LintelAssemblyCreationGate.CanStart([]));
    }

    [Fact]
    public void IsCurrentSelection_IgnoresOrderButDetectsChanges()
    {
        Assert.True(LintelAssemblyCreationGate.IsCurrentSelection([100, 200], [200, 100]));
        Assert.False(LintelAssemblyCreationGate.IsCurrentSelection([100, 200], [100, 300]));
    }

    [Fact]
    public void CanCreateOrFormatViews_AllowsBatchWhenEveryTypeHasExistingAssembly()
    {
        LintelTypeDiagnostic existing = CreateType(100) with
        {
            ExistingAssemblyName = "TB_Перемычка_ПР-1_100"
        };
        LintelTypeDiagnostic newType = CreateType(200);

        LintelTypeDiagnostic secondExisting = CreateType(300) with
        {
            ExistingAssemblyName = "TB_Перемычка_ПР-2_300"
        };

        Assert.True(LintelAssemblyCreationGate.CanCreateOrFormatViews([existing]));
        Assert.True(LintelAssemblyCreationGate.CanCreateOrFormatViews([existing, secondExisting]));
        Assert.False(LintelAssemblyCreationGate.CanCreateOrFormatViews([]));
        Assert.False(LintelAssemblyCreationGate.CanCreateOrFormatViews([newType]));
        Assert.False(LintelAssemblyCreationGate.CanCreateOrFormatViews([existing, newType]));
    }

    private static LintelTypeDiagnostic CreateType(long typeId)
    {
        return new LintelTypeDiagnostic(
            typeId,
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
