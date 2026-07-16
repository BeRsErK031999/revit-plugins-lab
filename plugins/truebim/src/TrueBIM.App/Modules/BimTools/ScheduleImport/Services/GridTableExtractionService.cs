using System.Text.RegularExpressions;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed record TableSourceWord(
    string Text,
    double Left,
    double Bottom,
    double Right,
    double Top)
{
    public double CenterX => (Left + Right) / 2;

    public double CenterY => (Bottom + Top) / 2;

    public double Height => Math.Abs(Top - Bottom);
}

public sealed record TableSourceLine(
    double X1,
    double Y1,
    double X2,
    double Y2);

public sealed class GridTableExtractionService
{
    private const int MinimumBoundaryCount = 4;
    private const int MaximumTableRows = 200;

    public ParsedTable? Extract(
        string sourceFilePath,
        int pageNumber,
        IReadOnlyList<TableSourceWord> words,
        IReadOnlyList<TableSourceLine> lines,
        IReadOnlyList<string> warnings)
    {
        if (lines.Count == 0 || words.Count == 0)
        {
            return null;
        }

        double extent = CalculateExtent(words, lines);
        double tolerance = Math.Max(0.05, extent * 0.0016);
        IReadOnlyList<AxisGroup> horizontal = BuildAxisGroups(lines, horizontal: true, tolerance, extent);
        IReadOnlyList<AxisGroup> vertical = BuildAxisGroups(lines, horizontal: false, tolerance, extent);
        GridBand? band = FindHeaderBand(horizontal, vertical, tolerance);
        if (band is null)
        {
            return null;
        }

        IReadOnlyList<double> xBoundaries = band.VerticalGroups
            .Select(group => group.Coordinate)
            .OrderBy(value => value)
            .ToList();
        IReadOnlyList<double> yBoundaries = FindRowBoundaries(
            horizontal,
            band.VerticalGroups,
            band.Top,
            band.Bottom,
            xBoundaries[0],
            xBoundaries[xBoundaries.Count - 1],
            tolerance);
        if (xBoundaries.Count < MinimumBoundaryCount || yBoundaries.Count < 2)
        {
            return null;
        }

        List<List<string>> matrix = BuildMatrix(words, xBoundaries, yBoundaries, tolerance);
        int lastNonEmptyRow = FindLastNonEmptyRow(matrix);
        if (lastNonEmptyRow < 1)
        {
            return null;
        }

        matrix = matrix.Take(lastNonEmptyRow + 1).ToList();
        yBoundaries = yBoundaries.Take(lastNonEmptyRow + 2).ToList();
        IReadOnlyList<string> columns = BuildColumnNames(matrix[0]);
        List<ParsedRow> rows = [];
        List<ParsedCell> cells = [];
        for (int rowIndex = 0; rowIndex < matrix.Count; rowIndex++)
        {
            rows.Add(new ParsedRow(rowIndex, matrix[rowIndex]));
            double top = yBoundaries[rowIndex];
            double bottom = yBoundaries[rowIndex + 1];
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                double left = xBoundaries[columnIndex];
                double right = xBoundaries[columnIndex + 1];
                cells.Add(new ParsedCell(
                    rowIndex,
                    columnIndex,
                    1,
                    1,
                    matrix[rowIndex][columnIndex],
                    new ParsedCellBoundingBox(
                        left,
                        Math.Min(top, bottom),
                        Math.Abs(right - left),
                        Math.Abs(top - bottom)),
                    0.94,
                    rowIndex == 0));
            }
        }

        return new ParsedTable(
            sourceFilePath,
            Math.Max(1, pageNumber),
            rows,
            columns,
            cells,
            0.94,
            warnings);
    }

    private static double CalculateExtent(
        IReadOnlyList<TableSourceWord> words,
        IReadOnlyList<TableSourceLine> lines)
    {
        double minX = lines.SelectMany(line => new[] { line.X1, line.X2 })
            .Concat(words.Select(word => word.Left))
            .DefaultIfEmpty(0)
            .Min();
        double maxX = lines.SelectMany(line => new[] { line.X1, line.X2 })
            .Concat(words.Select(word => word.Right))
            .DefaultIfEmpty(1)
            .Max();
        double minY = lines.SelectMany(line => new[] { line.Y1, line.Y2 })
            .Concat(words.Select(word => word.Bottom))
            .DefaultIfEmpty(0)
            .Min();
        double maxY = lines.SelectMany(line => new[] { line.Y1, line.Y2 })
            .Concat(words.Select(word => word.Top))
            .DefaultIfEmpty(1)
            .Max();
        return Math.Max(1, Math.Max(maxX - minX, maxY - minY));
    }

    private static IReadOnlyList<AxisGroup> BuildAxisGroups(
        IReadOnlyList<TableSourceLine> lines,
        bool horizontal,
        double tolerance,
        double extent)
    {
        double minimumLength = Math.Max(tolerance * 4, extent * 0.015);
        List<AxisSegment> segments = [];
        foreach (TableSourceLine line in lines)
        {
            double dx = Math.Abs(line.X2 - line.X1);
            double dy = Math.Abs(line.Y2 - line.Y1);
            if (horizontal && dy <= tolerance && dx >= minimumLength)
            {
                segments.Add(new AxisSegment(
                    (line.Y1 + line.Y2) / 2,
                    Math.Min(line.X1, line.X2),
                    Math.Max(line.X1, line.X2)));
            }
            else if (!horizontal && dx <= tolerance && dy >= minimumLength)
            {
                segments.Add(new AxisSegment(
                    (line.X1 + line.X2) / 2,
                    Math.Min(line.Y1, line.Y2),
                    Math.Max(line.Y1, line.Y2)));
            }
        }

        List<AxisGroup> groups = [];
        foreach (AxisSegment segment in segments.OrderBy(segment => segment.Coordinate))
        {
            AxisGroup? group = groups.LastOrDefault();
            if (group is null || Math.Abs(group.Coordinate - segment.Coordinate) > tolerance)
            {
                groups.Add(new AxisGroup(segment.Coordinate, [new Interval(segment.Start, segment.End)]));
                continue;
            }

            group.Add(segment);
        }

        return groups;
    }

    private static GridBand? FindHeaderBand(
        IReadOnlyList<AxisGroup> horizontal,
        IReadOnlyList<AxisGroup> vertical,
        double tolerance)
    {
        IReadOnlyList<AxisGroup> ordered = horizontal
            .OrderByDescending(group => group.Coordinate)
            .ToList();
        List<GridBand> candidates = [];
        for (int topIndex = 0; topIndex < ordered.Count - 1; topIndex++)
        {
            AxisGroup top = ordered[topIndex];
            int bottomLimit = Math.Min(ordered.Count, topIndex + 9);
            for (int bottomIndex = topIndex + 1; bottomIndex < bottomLimit; bottomIndex++)
            {
                AxisGroup bottom = ordered[bottomIndex];
                if (top.Coordinate - bottom.Coordinate <= tolerance * 2)
                {
                    continue;
                }

                IReadOnlyList<AxisGroup> crossing = vertical
                    .Where(group => group.Coverage(bottom.Coordinate, top.Coordinate, tolerance) >= 0.8)
                    .OrderBy(group => group.Coordinate)
                    .ToList();
                if (crossing.Count < MinimumBoundaryCount)
                {
                    continue;
                }

                double left = crossing[0].Coordinate;
                double right = crossing[crossing.Count - 1].Coordinate;
                if (right - left <= tolerance * 10
                    || top.Coverage(left, right, tolerance) < 0.72
                    || bottom.Coverage(left, right, tolerance) < 0.72)
                {
                    continue;
                }

                candidates.Add(new GridBand(
                    top.Coordinate,
                    bottom.Coordinate,
                    crossing,
                    crossing.Count,
                    right - left));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.BoundaryCount)
            .ThenByDescending(candidate => candidate.Width)
            .ThenByDescending(candidate => candidate.Top)
            .FirstOrDefault();
    }

    private static IReadOnlyList<double> FindRowBoundaries(
        IReadOnlyList<AxisGroup> horizontal,
        IReadOnlyList<AxisGroup> verticalBoundaries,
        double top,
        double firstBottom,
        double left,
        double right,
        double tolerance)
    {
        List<double> result = [top];
        IReadOnlyList<AxisGroup> candidates = horizontal
            .Where(group => group.Coordinate < top - tolerance)
            .Where(group => group.Coverage(left, right, tolerance) >= 0.72)
            .OrderByDescending(group => group.Coordinate)
            .ToList();
        double previous = top;
        double initialHeight = Math.Max(tolerance * 4, top - firstBottom);
        foreach (AxisGroup candidate in candidates)
        {
            double gap = previous - candidate.Coordinate;
            if (gap <= tolerance)
            {
                continue;
            }

            if (result.Count > 1 && gap > initialHeight * 5)
            {
                break;
            }

            int connectedBoundaries = verticalBoundaries.Count(group =>
                group.Coverage(candidate.Coordinate, previous, tolerance) >= 0.72);
            int requiredBoundaries = Math.Max(2, (int)Math.Ceiling(verticalBoundaries.Count * 0.6));
            if (connectedBoundaries < requiredBoundaries)
            {
                if (result.Count > 1)
                {
                    break;
                }

                continue;
            }

            result.Add(candidate.Coordinate);
            previous = candidate.Coordinate;
            if (result.Count >= MaximumTableRows + 1)
            {
                break;
            }
        }

        return result;
    }

    private static List<List<string>> BuildMatrix(
        IReadOnlyList<TableSourceWord> words,
        IReadOnlyList<double> xBoundaries,
        IReadOnlyList<double> yBoundaries,
        double tolerance)
    {
        List<List<string>> matrix = [];
        for (int rowIndex = 0; rowIndex < yBoundaries.Count - 1; rowIndex++)
        {
            double top = yBoundaries[rowIndex];
            double bottom = yBoundaries[rowIndex + 1];
            List<string> row = [];
            for (int columnIndex = 0; columnIndex < xBoundaries.Count - 1; columnIndex++)
            {
                double left = xBoundaries[columnIndex];
                double right = xBoundaries[columnIndex + 1];
                IReadOnlyList<TableSourceWord> cellWords = words
                    .Where(word => word.CenterX >= left - tolerance && word.CenterX <= right + tolerance)
                    .Where(word => word.CenterY <= top + tolerance && word.CenterY >= bottom - tolerance)
                    .ToList();
                row.Add(BuildCellText(cellWords, tolerance));
            }

            matrix.Add(row);
        }

        return matrix;
    }

    private static string BuildCellText(IReadOnlyList<TableSourceWord> words, double tolerance)
    {
        if (words.Count == 0)
        {
            return string.Empty;
        }

        double medianHeight = words
            .Select(word => Math.Max(tolerance, word.Height))
            .OrderBy(value => value)
            .ElementAt(words.Count / 2);
        double lineTolerance = Math.Max(tolerance, medianHeight * 0.6);
        List<List<TableSourceWord>> lines = [];
        foreach (TableSourceWord word in words.OrderByDescending(word => word.CenterY).ThenBy(word => word.Left))
        {
            List<TableSourceWord>? line = lines.FirstOrDefault(candidate =>
                Math.Abs(candidate.Average(item => item.CenterY) - word.CenterY) <= lineTolerance);
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        return string.Join(
            Environment.NewLine,
            lines
                .OrderByDescending(line => line.Average(word => word.CenterY))
                .Select(line => NormalizeText(string.Join(" ", line.OrderBy(word => word.Left).Select(word => word.Text))))
                .Where(text => text.Length > 0));
    }

    private static int FindLastNonEmptyRow(IReadOnlyList<List<string>> matrix)
    {
        for (int rowIndex = matrix.Count - 1; rowIndex >= 0; rowIndex--)
        {
            if (matrix[rowIndex].Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> BuildColumnNames(IReadOnlyList<string> header)
    {
        HashSet<string> used = new(StringComparer.CurrentCultureIgnoreCase);
        List<string> result = [];
        for (int index = 0; index < header.Count; index++)
        {
            string baseName = ScheduleColumnHeadingNormalizer.Normalize(header[index]);
            if (baseName.Length == 0)
            {
                baseName = $"Колонка {index + 1}";
            }

            string name = baseName;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = $"{baseName} {suffix++}";
            }

            result.Add(name);
        }

        return result;
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value!;
        return string.Join(
            Environment.NewLine,
            normalized
                .Replace("\u00a0", " ")
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
                .Where(line => line.Length > 0));
    }

    private sealed record AxisSegment(double Coordinate, double Start, double End);

    private sealed record Interval(double Start, double End);

    private sealed class AxisGroup
    {
        private int count = 1;

        public AxisGroup(double coordinate, List<Interval> intervals)
        {
            Coordinate = coordinate;
            Intervals = intervals;
        }

        public double Coordinate { get; private set; }

        public List<Interval> Intervals { get; }

        public void Add(AxisSegment segment)
        {
            Coordinate = (Coordinate * count + segment.Coordinate) / (count + 1);
            count++;
            Intervals.Add(new Interval(segment.Start, segment.End));
        }

        public double Coverage(double start, double end, double tolerance)
        {
            double minimum = Math.Min(start, end);
            double maximum = Math.Max(start, end);
            if (maximum - minimum <= tolerance)
            {
                return 0;
            }

            List<Interval> clipped = Intervals
                .Select(interval => new Interval(
                    Math.Max(minimum, interval.Start - tolerance),
                    Math.Min(maximum, interval.End + tolerance)))
                .Where(interval => interval.End > interval.Start)
                .OrderBy(interval => interval.Start)
                .ToList();
            if (clipped.Count == 0)
            {
                return 0;
            }

            double covered = 0;
            double currentStart = clipped[0].Start;
            double currentEnd = clipped[0].End;
            foreach (Interval interval in clipped.Skip(1))
            {
                if (interval.Start <= currentEnd + tolerance)
                {
                    currentEnd = Math.Max(currentEnd, interval.End);
                    continue;
                }

                covered += currentEnd - currentStart;
                currentStart = interval.Start;
                currentEnd = interval.End;
            }

            covered += currentEnd - currentStart;
            return Math.Min(1, covered / (maximum - minimum));
        }
    }

    private sealed record GridBand(
        double Top,
        double Bottom,
        IReadOnlyList<AxisGroup> VerticalGroups,
        int BoundaryCount,
        double Width);
}
