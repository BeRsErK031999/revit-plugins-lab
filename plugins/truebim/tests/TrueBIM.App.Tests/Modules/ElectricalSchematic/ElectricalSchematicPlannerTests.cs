using TrueBIM.App.Modules.ElectricalSchematic.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.ElectricalSchematic;

public sealed class ElectricalSchematicPlannerTests
{
    [Theory]
    [InlineData(" Гр.1.4c ", "1.4с")]
    [InlineData("гр. 1.4C", "1.4С")]
    [InlineData("1.4с", "1.4С")]
    public void EquivalentGroupsMatch(string left, string right) =>
        Assert.Equal(ElectricalSchematicPlanner.NormalizeGroup(left), ElectricalSchematicPlanner.NormalizeGroup(right));

    [Fact]
    public void InternalSpacesAreNotRemoved() => Assert.NotEqual(
        ElectricalSchematicPlanner.NormalizeGroup("1. 4с"), ElectricalSchematicPlanner.NormalizeGroup("1.4с"));

    [Fact]
    public void SampleUsesWattsDividedByThousandAndSortedNames()
    {
        ElectricalSchematicRow row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme()],
            [new(599575, "1.4c", 1000, "1.1"), new(602086, "Гр.1.4с", 2000, "1.3")]));
        Assert.True(row.CanApply);
        Assert.Equal(3, row.Power);
        Assert.Equal(2, row.Count);
        Assert.Equal("1.1, 1.3", row.Consumers);
        Assert.False(row.IsSelected);
    }

    [Fact]
    public void DuplicateNamesKeepDistinctLoadsButDuplicateElementIsCountedOnce()
    {
        ElectricalConsumer source = new(1, "1.4с", 1000, " A ");
        var row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme()], [source, source, new(2, "1.4с", 2000, "A")]));
        Assert.Equal(3, row.Power);
        Assert.Equal(2, row.Count);
        Assert.Equal("A", row.Consumers);
    }

    [Fact]
    public void UnselectedDuplicateOnOtherViewBlocksBothSchemes()
    {
        var rows = ElectricalSchematicPlanner.Build([Scheme(), Scheme() with { Id = 2, Group = "Гр.1.4c", View = "Другой вид" }],
            [new(1, "1.4с", 1000, "A")]);
        Assert.All(rows, row => { Assert.False(row.CanApply); Assert.Null(row.Power); Assert.Contains("несколько", row.Message); });
    }

    [Fact]
    public void NoSourcesPreservesPreviousValues()
    {
        var row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme()], []));
        Assert.False(row.CanApply);
        Assert.Null(row.Power);
        Assert.Equal(9, row.Scheme.Power);
        Assert.Equal("Ранее", row.PreviousConsumers);
    }

    [Fact]
    public void EmptyGroupDoesNotMatchEmptySourceGroup()
    {
        var row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme() with { Group = " " }], [new(1, "", 1000, "A")]));
        Assert.False(row.CanApply);
        Assert.Contains("не заполнена", row.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSourceBlocksWholeGroup(double? watts)
    {
        var row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme()], [new(1, "1.4с", watts, "A"), new(2, "1.4с", 1000, "B")]));
        Assert.False(row.CanApply);
        Assert.Null(row.Power);
    }

    [Fact]
    public void ZeroIsWrittenAndBlankNamesWarn()
    {
        var row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme()], [new(1, "1.4с", 0, " ")]));
        Assert.True(row.CanApply);
        Assert.Equal(0, row.Power);
        Assert.Equal("", row.Consumers);
        Assert.Contains("пустые", row.Message);
    }

    [Fact]
    public void TargetErrorAndSourceErrorPreventWriting()
    {
        var row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme() with { Error = "Только чтение" }], [new(1, "1.4с", 1000, "A")]));
        Assert.False(row.CanApply);
        Assert.Equal("Только чтение", row.Message);
        row = Assert.Single(ElectricalSchematicPlanner.Build([Scheme()], [new(1, "1.4с", 1000, "A", "Нет параметра")]));
        Assert.False(row.CanApply);
        Assert.Contains("Нет параметра", row.Message);
    }

    [Fact]
    public void OtherGroupsDoNotContributeAndBadGroupDoesNotBlockGoodGroup()
    {
        var rows = ElectricalSchematicPlanner.Build([Scheme(), Scheme() with { Id = 2, Group = "2" }],
            [new(1, "1.4с", 1000, "A"), new(2, "2", null, "B"), new(3, "", 5000, "C")]);
        Assert.Equal(1, rows.Single(row => row.Id == 588397).Power);
        Assert.False(rows.Single(row => row.Id == 2).CanApply);
    }

    private static ElectricalScheme Scheme() => new(588397, "1.4с", "ЩОС-2", 9, "Ранее");
}
