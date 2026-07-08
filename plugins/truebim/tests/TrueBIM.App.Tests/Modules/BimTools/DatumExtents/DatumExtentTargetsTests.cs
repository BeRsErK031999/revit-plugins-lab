using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.DatumExtents;

public sealed class DatumExtentTargetsTests
{
    [Fact]
    public void GetDisplayName_MapsProfileValues()
    {
        Assert.Equal("3D модельные экстенты", DatumExtentTargets.GetDisplayName(DatumExtentTargets.Model));
        Assert.Equal("2D на активном виде", DatumExtentTargets.GetDisplayName(DatumExtentTargets.ViewSpecific));
        Assert.Equal("2D на активном виде", DatumExtentTargets.GetDisplayName("unknown"));
    }

    [Fact]
    public void NormalizeProfileValue_DefaultsToViewSpecific()
    {
        Assert.Equal(DatumExtentTargets.Model, DatumExtentTargets.NormalizeProfileValue("model"));
        Assert.Equal(DatumExtentTargets.ViewSpecific, DatumExtentTargets.NormalizeProfileValue("unknown"));
        Assert.Equal(DatumExtentTargets.ViewSpecific, DatumExtentTargets.NormalizeProfileValue(null));
    }

    [Fact]
    public void Options_ExposeTwoTargets()
    {
        Assert.Contains(DatumExtentTargets.Options, option => option.Value == DatumExtentTargets.Model);
        Assert.Contains(DatumExtentTargets.Options, option => option.Value == DatumExtentTargets.ViewSpecific);
    }
}
