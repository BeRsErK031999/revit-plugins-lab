using System.IO;
using TrueBIM.App.Modules.GeometricFinishes;
using Xunit;

namespace TrueBIM.App.Tests.Modules.GeometricFinishes;

public sealed class FinishSettingsTests
{
    [Fact]
    public void PersistedProjectSetupRetainsExcludedRowsAndRoomBounding()
    {
        string root = Path.Combine(Path.GetTempPath(), "TrueBIM-FinishSettings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "walls.xml");
        try
        {
            FinishSettings settings = new()
            {
                Kind = FinishKind.Walls, Scope = FinishScope.Level, LevelUniqueId = "level-2", Grouping = FinishGrouping.RoomAndWallType,
                OffsetMm = -80, RoomFilter = "С/У", RoomParameterKey = "p:-1006901", FinishParameterKey = "g:" + Guid.NewGuid(),
                Assignments = new() { new() { Key = "санузел / бетон", TypeUniqueId = "type-1", Enabled = false, RoomBounding = true } }
            };
            FinishSettingsStore.Save(path, settings);
            settings.OffsetMm = 25;
            FinishSettingsStore.Save(path, settings); // Atomic replacement of an existing profile.
            FinishSettings restored = FinishSettingsStore.Load(path, FinishKind.Walls, out string? warning);
            Assert.Null(warning);
            Assert.Equal(FinishScope.Level, restored.Scope);
            Assert.Equal("level-2", restored.LevelUniqueId);
            Assert.Equal("С/У", restored.RoomFilter);
            Assert.Equal(25, restored.OffsetMm);
            Assert.Equal(settings.FinishParameterKey, restored.FinishParameterKey);
            Assert.True(Assert.Single(restored.Assignments).RoomBounding);
            Assert.False(restored.Assignments[0].Enabled);
            Assert.Single(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(100001)]
    [InlineData(-100001)]
    public void InvalidHeightCannotReachModelCreation(double offset)
    {
        Assert.NotNull(new FinishSettings { OffsetMm = offset }.Validate());
    }

    [Fact]
    public void IncompleteParameterMappingAndMissingLevelAreRejected()
    {
        Assert.NotNull(new FinishSettings { RoomParameterKey = "room-number" }.Validate());
        Assert.NotNull(new FinishSettings { FinishParameterKey = "comments" }.Validate());
        Assert.NotNull(new FinishSettings { Scope = FinishScope.Level }.Validate());
        Assert.Null(new FinishSettings { Scope = FinishScope.Level, LevelUniqueId = "level-1" }.Validate());
    }

    [Fact]
    public void RoomAndWallKeysCannotCollideWhenNamesContainSeparators()
    {
        string a = FinishSettings.AssignmentKey(FinishGrouping.RoomAndWallType, "a:b", "c");
        string b = FinishSettings.AssignmentKey(FinishGrouping.RoomAndWallType, "a", "b:c");
        Assert.NotEqual(a, b);
        Assert.Equal(FinishSettings.AssignmentKey(FinishGrouping.WallType, "Кухня", "wall-A"),
            FinishSettings.AssignmentKey(FinishGrouping.WallType, "Коридор", "wall-A"));
        Assert.NotEqual(FinishSettings.AssignmentKey(FinishGrouping.RoomName, "Кухня", "wall-A"),
            FinishSettings.AssignmentKey(FinishGrouping.RoomName, "Коридор", "wall-A"));
    }

    [Theory]
    [InlineData("<broken>")]
    [InlineData("<!DOCTYPE x [<!ENTITY attack SYSTEM 'file:///C:/Windows/win.ini'>]><FinishSettings>&attack;</FinishSettings>")]
    public void CorruptOrExternalEntityProfilesAreNotApplied(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), "TrueBIM-FinishSettings-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(path, xml);
            FinishSettings settings = FinishSettingsStore.Load(path, FinishKind.Walls, out string? warning);
            Assert.NotNull(warning);
            Assert.Equal(FinishKind.Walls, settings.Kind);
            Assert.Equal(FinishGrouping.WallType, settings.Grouping);
            Assert.Empty(settings.Assignments);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SettingsAreSeparatedByProjectAndCommand()
    {
        string floor = FinishSettingsStore.PathFor("C:\\ProjectA\\Model.rvt", FinishKind.Floors);
        Assert.NotEqual(floor, FinishSettingsStore.PathFor("C:\\ProjectB\\Model.rvt", FinishKind.Floors));
        Assert.NotEqual(floor, FinishSettingsStore.PathFor("C:\\ProjectA\\Model.rvt", FinishKind.Walls));
        Assert.DoesNotContain("ProjectA", floor);
    }
}
