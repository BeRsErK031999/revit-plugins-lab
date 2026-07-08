using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashStatusesTests
{
    [Theory]
    [InlineData("new", ClashStatus.Open)]
    [InlineData("active", ClashStatus.InProgress)]
    [InlineData("approved", ClashStatus.Approved)]
    [InlineData("resolved", ClashStatus.Resolved)]
    [InlineData("ignored", ClashStatus.Ignored)]
    public void Parse_SupportsClashManagerWorkflowNames(string value, ClashStatus expected)
    {
        Assert.Equal(expected, ClashStatuses.Parse(value));
    }

    [Fact]
    public void ToDisplayName_UsesClashManagerWorkflowNames()
    {
        Assert.Equal("New", ClashStatuses.ToDisplayName(ClashStatus.Open));
        Assert.Equal("Active", ClashStatuses.ToDisplayName(ClashStatus.InProgress));
        Assert.Equal("Approved", ClashStatuses.ToDisplayName(ClashStatus.Approved));
    }
}
