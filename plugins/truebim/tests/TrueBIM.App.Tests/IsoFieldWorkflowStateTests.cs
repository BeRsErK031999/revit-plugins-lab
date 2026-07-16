using TrueBIM.App.Modules.IsoFieldRebar.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldWorkflowStateTests
{
    [Fact]
    public void EmptyWorkflow_OnlyRequestsSource()
    {
        IsoFieldWorkflowState state = new(false, false, false, false, false, false, false);

        Assert.Equal(0, state.CompletedStepCount);
        Assert.False(state.CanRunRecognition);
        Assert.False(state.CanCalculateRules);
        Assert.False(state.CanCreateRebar);
        Assert.Contains("JSON", state.NextAction, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageWithoutProcessor_ExplainsProcessingBlocker()
    {
        IsoFieldWorkflowState state = new(true, false, false, false, false, false, false);

        Assert.False(state.CanRunRecognition);
        Assert.Contains("обработчик", state.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidRules_EnableControlledCreation()
    {
        IsoFieldWorkflowState state = new(true, true, true, true, true, true, true);

        Assert.Equal(5, state.CompletedStepCount);
        Assert.True(state.CanRunRecognition);
        Assert.True(state.CanShowRevitPreview);
        Assert.True(state.CanClearRevitPreview);
        Assert.True(state.CanCalculateRules);
        Assert.True(state.CanCreateRebar);
    }

    [Fact]
    public void UnconfirmedLayerMappings_BlockCreationWithSpecificNextAction()
    {
        IsoFieldWorkflowState state = new(true, true, true, true, false, true, false);

        Assert.False(state.CanCreateRebar);
        Assert.Contains("верх/низ", state.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SlabWithoutValidBinding_BlocksRulesAndCreation()
    {
        IsoFieldWorkflowState state = new(
            true,
            true,
            true,
            true,
            false,
            true,
            true,
            HasValidHostBinding: false);

        Assert.False(state.HasReadyHost);
        Assert.False(state.CanCalculateRules);
        Assert.False(state.CanCreateRebar);
        Assert.Contains("двум контрольным точкам", state.NextAction, StringComparison.OrdinalIgnoreCase);
    }
}
