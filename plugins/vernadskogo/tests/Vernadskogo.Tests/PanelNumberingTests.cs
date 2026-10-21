using System;
using System.Collections.Generic;
using System.Linq;
using Vernadskogo.App.Core;
using Xunit;

namespace Vernadskogo.Tests;

public sealed class PanelNumberingTests
{
    private static readonly Dictionary<string, string> Materials = new() { ["a"] = "1", ["b"] = "2" };

    [Theory]
    [InlineData("ВЕР_К1_АР.rvt", "1")]
    [InlineData("ВЕР_К2_АР_username", "2")]
    [InlineData("ВЕР_К12_АР", "12")]
    [InlineData("ВЕР_К01_АР", "1")]
    public void ReadsBuildingToken(string name, string expected) => Assert.Equal(expected, PanelNumbering.ParseBuilding(name));

    [Theory]
    [InlineData("ВЕР_АР")]
    [InlineData("ВЕР_К1_К2_АР")]
    [InlineData("ВЕР_К1_АР_К2_Модель")]
    [InlineData("ВЕР_K1_АР")]
    [InlineData("ВЕР_К1.rvt")]
    public void RejectsMissingOrAmbiguousBuilding(string name) => Assert.Throws<InvalidOperationException>(() => PanelNumbering.ParseBuilding(name));

    [Theory]
    [InlineData("К1_Этаж 13_+46,240", "13")]
    [InlineData("К1_Этаж 10_+35,020", "10")]
    [InlineData("К2_Этаж 01_+0,000", "1")]
    public void ReadsFloor(string name, string expected) => Assert.Equal(expected, PanelNumbering.ParseFloor(name));

    [Theory]
    [InlineData("К1_Кровля_+46,240")]
    [InlineData("К1_Этаж -1_-3,000")]
    [InlineData("К1_Этаж 13а_+46,240")]
    [InlineData("К1_Этаж 1_Этаж 2_")]
    public void RejectsUnrecognizedFloor(string name) => Assert.Throws<InvalidOperationException>(() => PanelNumbering.ParseFloor(name));

    [Theory]
    [InlineData(StartDirection.South, 0)]
    [InlineData(StartDirection.SouthEast, 1)]
    [InlineData(StartDirection.East, 2)]
    [InlineData(StartDirection.NorthEast, 3)]
    [InlineData(StartDirection.North, 4)]
    [InlineData(StartDirection.NorthWest, 5)]
    [InlineData(StartDirection.West, 6)]
    [InlineData(StartDirection.SouthWest, 7)]
    public void AllEightDirectionsStartOnTheirRayAndContinueCounterclockwise(StartDirection direction, int first)
    {
        var positions = new[] { (0d, -10d), (10d, -10d), (10d, 0d), (10d, 10d),
            (0d, 10d), (-10d, 10d), (-10d, 0d), (-10d, -10d) };
        var panels = positions.Select((p, i) => Point(i, "13", p.Item1, p.Item2)).Reverse().ToArray();
        var marks = PanelNumbering.CreateMarks(panels, "1", Materials, direction);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => (long)((first + i) % 8)), marks.Select(m => m.Panel.Id));
        Assert.Equal("1_1_13_1", marks[0].Mark);
    }

    [Fact]
    public void UsesEachFloorsOwnEnvelopeAndRestartsNumbersRegardlessOfMaterial()
    {
        var panels = new[]
        {
            Point(1, "10", 0, -10, "a"), Point(2, "10", 10, 0, "b"),
            Point(3, "10", 0, 10, "a"), Point(4, "10", -10, 0, "b"),
            Point(5, "13", 1000, 990, "b"), Point(6, "13", 1010, 1000, "a"),
            Point(7, "13", 1000, 1010, "b"), Point(8, "13", 990, 1000, "a")
        };
        var marks = PanelNumbering.CreateMarks(panels.Reverse().ToArray(), "2", Materials, StartDirection.South)
            .ToDictionary(m => m.Panel.Id);
        Assert.Equal("1_2_10_1", marks[1].Mark);
        Assert.Equal("2_2_10_2", marks[2].Mark);
        Assert.Equal("2_2_13_1", marks[5].Mark);
        Assert.Equal("1_2_13_2", marks[6].Mark);
        Assert.Equal("К2", marks[6].Section);
    }

    [Fact]
    public void CenterUsesExtentsRatherThanAveragePanelPositions()
    {
        var panels = new[] { Point(1, "1", -10, -10), Point(2, "1", 10, -10),
            Point(3, "1", 10, 10), Point(4, "1", -10, 10),
            Point(5, "1", 2, -10), Point(6, "1", 4, -10), Point(7, "1", 6, -10), Point(8, "1", 8, -10) };
        var marks = PanelNumbering.CreateMarks(panels, "1", Materials, StartDirection.South);
        Assert.Equal(5, marks[0].Panel.Id);
        Assert.Equal(1, marks.Last().Panel.Id);
    }

    [Fact]
    public void CenterIncludesPanelBoundsNotJustPanelMidpoints()
    {
        var panels = new[] { new PanelPosition(1, "1", "a", -20, -10, 0, -10),
            Point(2, "1", 10, -10), Point(3, "1", 10, 10), Point(4, "1", -10, 10), Point(5, "1", -3, -10) };
        Assert.Equal(5, PanelNumbering.CreateMarks(panels, "1", Materials, StartDirection.South)[0].Panel.Id);
    }

    [Fact]
    public void EqualAnglesUseDistanceThenElementIdDeterministically()
    {
        var panels = new[] { Point(20, "1", 0, -10), Point(10, "1", 0, -10),
            Point(30, "1", 0, -5), Point(40, "1", 0, 10) };
        var marks = PanelNumbering.CreateMarks(panels, "1", Materials, StartDirection.South);
        Assert.Equal(new long[] { 30, 10, 20, 40 }, marks.Select(m => m.Panel.Id));
        Assert.Equal(marks.Select(m => m.Mark), PanelNumbering.CreateMarks(panels.Reverse().ToArray(), "1", Materials, StartDirection.South).Select(m => m.Mark));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1а")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1_2")]
    [InlineData("١")]
    public void MaterialNumbersContainAsciiDigitsOnly(string number) =>
        Assert.Throws<InvalidOperationException>(() => PanelNumbering.NormalizeMaterialNumber(number));

    [Fact]
    public void MaterialNumbersAreNormalizedAndCannotCollide()
    {
        Assert.Equal("1", PanelNumbering.NormalizeMaterialNumber(" 001 "));
        Assert.Throws<InvalidOperationException>(() => PanelNumbering.CreateMarks(new[] { Point(1, "1", 0, 0) },
            "1", new Dictionary<string, string> { ["a"] = "01", ["b"] = "1" }, StartDirection.South));
    }

    [Fact]
    public void MissingMaterialStopsNumbering() => Assert.Throws<InvalidOperationException>(() =>
        PanelNumbering.CreateMarks(new[] { Point(1, "1", 0, 0, "unknown") }, "1", Materials, StartDirection.South));

    private static PanelPosition Point(long id, string floor, double x, double y, string material = "a") =>
        new(id, floor, material, x, y, x, y);
}
