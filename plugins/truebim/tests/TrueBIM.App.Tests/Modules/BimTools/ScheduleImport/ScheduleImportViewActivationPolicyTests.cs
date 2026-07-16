using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleImportViewActivationPolicyTests
{
    [Fact]
    public void ShouldOpenSeparateTab_ReturnsTrueForSuccessfullyCreatedView()
    {
        DraftingTableCreationResult result = CreateResult(targetViewId: 42, createdNewView: true);

        Assert.True(ScheduleImportViewActivationPolicy.ShouldOpenSeparateTab(result));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0L, true)]
    [InlineData(42L, false)]
    public void ShouldOpenSeparateTab_ReturnsFalseWithoutCreatedView(long? targetViewId, bool createdNewView)
    {
        DraftingTableCreationResult result = CreateResult(targetViewId, createdNewView);

        Assert.False(ScheduleImportViewActivationPolicy.ShouldOpenSeparateTab(result));
    }

    private static DraftingTableCreationResult CreateResult(long? targetViewId, bool createdNewView)
    {
        return new DraftingTableCreationResult(
            "TrueBIM_Импорт таблицы",
            targetViewId,
            createdNewView,
            false,
            10,
            5,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }
}
