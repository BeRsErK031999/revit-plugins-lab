using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vernadskogo.App.Core;

public enum StartDirection
{
    South, SouthEast, East, NorthEast, North, NorthWest, West, SouthWest
}

public sealed class PanelPosition
{
    public PanelPosition(long id, string floor, string materialKey,
        double minX, double minY, double maxX, double maxY)
    {
        if (new[] { minX, minY, maxX, maxY }.Any(v => double.IsNaN(v) || double.IsInfinity(v))
            || minX > maxX || minY > maxY)
            throw new ArgumentException("Некорректные границы панели " + id + ".");
        Id = id;
        Floor = floor;
        MaterialKey = materialKey;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public long Id { get; }
    public string Floor { get; }
    public string MaterialKey { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double X => (MinX + MaxX) / 2;
    public double Y => (MinY + MaxY) / 2;
}

public sealed class PanelMark
{
    public PanelMark(PanelPosition panel, string buildingNumber, string materialNumber, int ordinal)
    {
        Panel = panel;
        Section = "К" + buildingNumber;
        Mark = string.Join("_", materialNumber, buildingNumber, panel.Floor,
            ordinal.ToString(CultureInfo.InvariantCulture));
    }

    public PanelPosition Panel { get; }
    public string Section { get; }
    public string Mark { get; }
}

public static class PanelNumbering
{
    public static string ParseBuilding(string modelName)
    {
        // Lookahead keeps the trailing underscore available for a following token.
        MatchCollection matches = Regex.Matches(modelName, @"_К([0-9]+)(?=_)", RegexOptions.CultureInvariant);
        string[] values = matches.Cast<Match>().Select(m => NormalizeDigits(m.Groups[1].Value)).Distinct().ToArray();
        if (values.Length != 1)
            throw new InvalidOperationException("Не удалось однозначно определить корпус из имени модели «"
                + modelName + "». Ожидается фрагмент _К1_, _К2_ и т. п.");
        return values[0];
    }

    public static string ParseFloor(string levelName)
    {
        MatchCollection matches = Regex.Matches(levelName,
            @"(?:^|[_\s])Этаж\s+([0-9]+)(?=$|[_\s])", RegexOptions.CultureInvariant);
        if (matches.Count != 1)
            throw new InvalidOperationException("Не удалось определить этаж из имени уровня «"
                + levelName + "». Ожидается, например, К1_Этаж 13_+46,240.");
        return NormalizeDigits(matches[0].Groups[1].Value);
    }

    public static string NormalizeMaterialNumber(string text)
    {
        string value = text.Trim();
        if (!Regex.IsMatch(value, @"\A[0-9]+\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Номер материала должен содержать только цифры.");
        return NormalizeDigits(value);
    }

    public static IReadOnlyList<PanelMark> CreateMarks(IReadOnlyList<PanelPosition> panels,
        string buildingNumber, IReadOnlyDictionary<string, string> materials, StartDirection direction)
    {
        var numbers = materials.ToDictionary(pair => pair.Key, pair => NormalizeMaterialNumber(pair.Value));
        if (numbers.Values.Distinct().Count() != numbers.Count)
            throw new InvalidOperationException("У разных материалов должны быть разные номера.");

        var result = new List<PanelMark>(panels.Count);
        foreach (var floor in panels.GroupBy(p => p.Floor))
        {
            double centerX = (floor.Min(p => p.MinX) + floor.Max(p => p.MaxX)) / 2;
            double centerY = (floor.Min(p => p.MinY) + floor.Max(p => p.MaxY)) / 2;
            var ordered = floor.OrderBy(p => Angle(p.X - centerX, p.Y - centerY, direction))
                .ThenBy(p => DistanceSquared(p.X - centerX, p.Y - centerY))
                .ThenBy(p => p.Id);
            int ordinal = 0;
            foreach (PanelPosition panel in ordered)
            {
                if (!numbers.TryGetValue(panel.MaterialKey, out string? materialNumber))
                    throw new InvalidOperationException("Не задан номер материала панели " + panel.Id + ".");
                result.Add(new PanelMark(panel, buildingNumber, materialNumber, ++ordinal));
            }
        }
        return result;
    }

    private static string NormalizeDigits(string value)
    {
        string trimmed = value.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    private static double Angle(double x, double y, StartDirection direction)
    {
        if (!Enum.IsDefined(typeof(StartDirection), direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        // Revit model axes: +Y = project north, +X = east. Increasing atan2 is CCW.
        double start = -Math.PI / 2 + (int)direction * Math.PI / 4;
        double angle = (Math.Atan2(y, x) - start) % (2 * Math.PI);
        if (angle < 0) angle += 2 * Math.PI;
        // Keep positions on the starting ray at the beginning despite rounding.
        return angle < 1e-10 || 2 * Math.PI - angle < 1e-10 ? 0 : angle;
    }

    private static double DistanceSquared(double x, double y) => x * x + y * y;
}
