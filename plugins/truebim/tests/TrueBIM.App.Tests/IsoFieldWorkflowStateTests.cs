using TrueBIM.App.Modules.IsoFieldRebar.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldWorkflowStateTests
{
    [Fact]
    public void EmptyWorkflow_OnlyRequestsSource()
    {
        IsoFieldWorkflowState state = new(false, false, false, false, false, false);

        Assert.Equal(0, state.CompletedStepCount);
        Assert.False(state.CanRunRecognition);
        Assert.False(state.CanCalculateRules);
        Assert.False(state.CanCreateRebar);
        Assert.Contains("JSON", state.NextAction, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageWithoutWorker_ExplainsProcessingBlocker()
    {
        IsoFieldWorkflowState state = new(true, false, false, false, false, false);

        Assert.False(state.CanRunRecognition);
        Assert.Contains("worker", state.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidRules_EnableControlledCreation()
    {
        IsoFieldWorkflowState state = new(true, true, true, true, true, true);

        Assert.Equal(4, state.CompletedStepCount);
        Assert.True(state.CanRunRecognition);
        Assert.True(state.CanShowRevitPreview);
        Assert.True(state.CanClearRevitPreview);
        Assert.True(state.CanCalculateRules);
        Assert.True(state.CanCreateRebar);
    }
}
