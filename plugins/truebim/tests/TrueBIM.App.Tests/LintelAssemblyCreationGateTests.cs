using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelAssemblyCreationGateTests
{
    [Fact]
    public void CanCreate_AllowsOneCurrentReadyType()
    {
        Assert.True(LintelAssemblyCreationGate.CanCreate(
            100,
            LintelAssemblyPreflightStatus.Ready,
            [100]));
    }

    [Theory]
    [InlineData(null, LintelAssemblyPreflightStatus.Ready)]
    [InlineData(100L, LintelAssemblyPreflightStatus.Blocked)]
    [InlineData(100L, LintelAssemblyPreflightStatus.AlreadyExists)]
    public void CanCreate_BlocksMissingOrUnreadyApproval(
        long? approvedTypeId,
        LintelAssemblyPreflightStatus status)
    {
        Assert.False(LintelAssemblyCreationGate.CanCreate(
            approvedTypeId,
            status,
            [100]));
    }

    [Fact]
    public void CanCreate_BlocksChangedOrMultipleSelection()
    {
        Assert.False(LintelAssemblyCreationGate.CanCreate(
            100,
            LintelAssemblyPreflightStatus.Ready,
            [101]));
        Assert.False(LintelAssemblyCreationGate.CanCreate(
            100,
            LintelAssemblyPreflightStatus.Ready,
            [100, 101]));
    }

    [Fact]
    public void IsCurrentSelection_IgnoresOrderButDetectsChanges()
    {
        Assert.True(LintelAssemblyCreationGate.IsCurrentSelection([100, 200], [200, 100]));
        Assert.False(LintelAssemblyCreationGate.IsCurrentSelection([100, 200], [100, 300]));
    }
}
