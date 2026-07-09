using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.DatumExtents;

public sealed class DatumExtentModesTests
{
    [Theory]
    [InlineData(DatumExtentMode.ViewSpecific, "2D на активном виде")]
    [InlineData(DatumExtentMode.Model, "3D модельные экстенты")]
    [InlineData(DatumExtentMode.Invert, "Инвертировать режим осей")]
    public void GetDisplayName_ReturnsUiLabel(
        DatumExtentMode mode,
        string expectedDisplayName)
    {
        Assert.Equal(expectedDisplayName, DatumExtentModes.GetDisplayName(mode));
    }
}
