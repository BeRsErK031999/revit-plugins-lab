using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashItemTests
{
    [Fact]
    public void GetResolvedElementIds_ReturnsCurrentModelElementIds()
    {
        ClashItem item = new("SELF-1-2", "Self clash", 1, 2, null, null, null, ClashStatus.Open, string.Empty)
        {
            IsElement1Resolved = true,
            IsElement2Resolved = true
        };

        Assert.Equal([1, 2], item.GetResolvedElementIds());
    }

    [Fact]
    public void GetResolvedElementIds_ReturnsLinkInstanceIdsForLinkedElements()
    {
        ClashItem item = new(
            "RVT-RVT-10-100-20-200",
            "Link clash",
            10,
            20,
            null,
            null,
            null,
            ClashStatus.Open,
            string.Empty,
            "Link A",
            "Link B",
            linkedElementId2: 200,
            linkedElementId1: 100,
            source: "RVT-связь ↔ RVT-связь")
        {
            IsElement1Resolved = true,
            IsElement2Resolved = true
        };

        Assert.Equal([10, 20], item.GetResolvedElementIds());
        Assert.Equal("100", item.ElementId1Text);
        Assert.Equal("200", item.ElementId2Text);
    }
}
