using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

/// <summary>
/// Managed, offline recognizer for the current PK LIRA raster export.
/// It extracts the horizontal color legend and envelopes of dense color regions.
/// </summary>
public sealed class BuiltInIsoFieldRecognitionRunner :
    IIsoFieldRecognitionRunner,
    IIsoFieldRecognitionRunnerDiagnostics
{
    private const int MaxLegendSearchHeight = 180;
    private const int MinimumLegendWidthDivisor = 5;
    private const int MinimumLegendBandWidthDivisor = 120;
    private const int ColorDistanceThreshold = 60;
    private const int DensityRadius = 2;
    private const int MinimumDensePixels = 13;
    private const int DilationRadius = 3;
    private const byte LegendTextThreshold = 220;
    private const int NormalizedGlyphWidth = 12;
    private const int NormalizedGlyphHeight = 18;

    private static readonly IReadOnlyList<GlyphTemplate> RasterGlyphTemplates =
    [
        new('0', [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."]),
        new('1', ["..#", "###", "..#", "..#", "..#", "..#", "..#", "..#", "..#"]),
        new('2', ["..###.", ".#...#", ".....#", ".....#", "....#.", "...#..", "..#...", ".#....", "######"]),
        new('3', [".###.", "#..##", "....#", "...#.", "..##.", "....#", "....#", "#...#", ".###."]),
        new('4', ["....#.", "...##.", "...##.", "..#.#.", ".#..#.", "....#.", "######", "....#.", "....#."]),
        new('5', ["#####", "#....", "#....", "####.", "#...#", "....#", "....#", "#...#", "####."]),
        new('6', [".###.", "#...#", "#....", "####.", "#...#", "#...#", "#...#", "#...#", ".###."]),
        new('7', ["#####", "....#", "...#.", "...#.", "...#.", "..#..", "..#..", ".#...", ".#..."]),
        new('8', [".###.", "#...#", "#...#", "#..#.", ".###.", "#...#", "#...#", "#...#", ".###."]),
        new('9', [".###.", "#..##", "#...#", "#...#", "#...#", ".####", "....#", "#..#.", ".###."]),
        new('d', ["....#", "....#", "....#", ".####", "#...#", "....#", "....#", "#..##", "#####"]),
        new('s', [".####", ".#..#", ".##..", "...##", "##..#", ".####"]),
        new('+', ["..#..", "..#..", "#####", "..#..", "..#.."])
    ];

    private static readonly IReadOnlyList<GlyphTemplate> NumericGlyphTemplates =
        RasterGlyphTemplates.Where(template => char.IsDigit(template.Character)).ToArray();

    private static readonly IReadOnlyList<ReinforcementLabelTemplate> ReinforcementLabelTemplates =
    [
        CreateReinforcementLabelTemplate("d10s200"),
        CreateReinforcementLabelTemplate("d10s200+d10s200"),
        CreateReinforcementLabelTemplate("d10s200+d12s200"),
        CreateReinforcementLabelTemplate("d10s200+d14s200"),
        CreateReinforcementLabelTemplate("d10s200+d16s200"),
        CreateReinforcementLabelTemplate("d10s200+d12s100"),
        CreateReinforcementLabelTemplate("d10s200+d14s100"),
        CreateReinforcementLabelTemplate("d10s200+d16s100"),
        CreateReinforcementLabelTemplate("d10s200+d20s100")
    ];

    public string RunnerName => "Встроенный";

    public IsoFieldRecognitionResult Run(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("IsoField image path is required.", nameof(sourcePath));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("IsoField image was not found.", sourcePath);
        }

        BitmapFrame frame = LoadFrame(sourcePath!);
        PixelBuffer buffer = PixelBuffer.Create(frame);
        IsoFieldLegend? legend = RecognizeLegend(buffer);
        if (legend is null || legend.Bands.Count < 2)
        {
            return new IsoFieldRecognitionResult(
                Array.Empty<IsoFieldPolyline>(),
                ["Цветовая шкала не найдена. Используйте исходный PNG ПК ЛИРА без обрезки заголовка."],
                Array.Empty<IsoFieldLegend>());
        }

        ZoneExtractionResult zones = ExtractZones(buffer, legend);
        List<string> diagnostics =
        [
            $"Встроенный распознаватель нашёл цветовую шкалу: уровней {legend.Bands.Count}.",
            $"Цветных зон после фильтрации: {zones.Polylines.Count}; шумовых компонентов отброшено: {zones.RejectedComponents}.",
            "Соседние цветные ячейки объединены; зоне присвоен максимальный уровень внутри контура."
        ];
        diagnostics.Add(legend.HasNumericRanges
            ? $"Числовые границы шкалы распознаны: {FormatValue(legend.Bands[0].MinimumValue!.Value)}–{FormatValue(legend.Bands[legend.Bands.Count - 1].MaximumValue!.Value)} см²/м."
            : "Числовые границы шкалы распознаны не полностью; зоны подписаны номером уровня и HEX-цветом.");
        diagnostics.Add(legend.HasReinforcementLabels
            ? $"Подписи сочетаний диаметр/шаг распознаны: {legend.EffectiveBoundaries.Count}; минимальное совпадение {legend.EffectiveBoundaries.Min(boundary => boundary.LabelConfidence!.Value):P0}."
            : "Подписи сочетаний диаметр/шаг распознаны не полностью и не были приняты.");
        if (zones.Polylines.Count == 0)
        {
            diagnostics.Add("Плотные цветные области не найдены. Проверьте, что расчётная карта не была сжата или изменена.");
        }

        return new IsoFieldRecognitionResult(zones.Polylines, diagnostics, [legend]);
    }

    private static BitmapFrame LoadFrame(string sourcePath)
    {
        using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static IsoFieldLegend? RecognizeLegend(PixelBuffer buffer)
    {
        int searchHeight = Math.Min(MaxLegendSearchHeight, Math.Max(1, buffer.Height / 4));
        int bestY = -1;
        int bestStartX = 0;
        int bestEndX = 0;
        int bestLength = 0;
        int bestTransitions = -1;
        for (int y = 0; y < searchHeight; y++)
        {
            (int startX, int endX) = FindLongestSaturatedRun(buffer, y);
            int length = endX >= startX ? endX - startX + 1 : 0;
            int transitions = length > 0
                ? CountStrongColorTransitions(buffer, y, startX, endX)
                : 0;
            if (length > bestLength || length == bestLength && transitions > bestTransitions)
            {
                bestY = y;
                bestStartX = startX;
                bestEndX = endX;
                bestLength = length;
                bestTransitions = transitions;
            }
        }

        if (bestY < 0 || bestLength < buffer.Width / MinimumLegendWidthDivisor)
        {
            return null;
        }

        IReadOnlyList<ColorRun> colorRuns = ExtractColorRuns(buffer, bestY, bestStartX, bestEndX);
        int minimumBandWidth = Math.Max(3, bestLength / MinimumLegendBandWidthDivisor);
        ColorRun[] bands = colorRuns
            .Where(run => run.Length >= minimumBandWidth)
            .ToArray();
        if (bands.Length < 2)
        {
            return null;
        }

        IsoFieldLegendBand[] legendBands = bands
            .Select((run, index) => new IsoFieldLegendBand(
                index,
                run.Red,
                run.Green,
                run.Blue,
                (double)(run.StartX - bestStartX) / bestLength,
                (double)(run.EndX - bestStartX + 1) / bestLength))
            .ToArray();
        List<IsoFieldLegendBoundary> boundaries =
        [
            new IsoFieldLegendBoundary(0, 0)
        ];
        boundaries.AddRange(legendBands.Select(
            (band, index) => new IsoFieldLegendBoundary(index + 1, band.EndRatio)));
        IsoFieldLegend legend = new(
            legendBands,
            bestY,
            bestStartX,
            bestEndX,
            Boundaries: boundaries);
        legend = ApplyNumericLegendRanges(buffer, legend);
        return ApplyReinforcementLabels(buffer, legend);
    }

    private static IsoFieldLegend ApplyNumericLegendRanges(PixelBuffer buffer, IsoFieldLegend legend)
    {
        (int top, int bottom)? rowBounds = FindLegendLabelRows(buffer, legend);
        if (rowBounds is null)
        {
            return legend;
        }

        int searchLeft = Math.Max(0, legend.PixelStartX - 24);
        int searchRight = Math.Min(buffer.Width - 1, legend.PixelEndX + 24);
        IReadOnlyList<BinaryGlyph> glyphs = FindBinaryGlyphs(
            buffer,
            searchLeft,
            searchRight,
            rowBounds.Value.top,
            rowBounds.Value.bottom);
        IReadOnlyList<NumericToken> tokens = RecognizeNumericTokens(glyphs, rowBounds.Value.bottom);
        int boundaryCount = legend.Bands.Count + 1;
        if (tokens.Count < boundaryCount)
        {
            return legend;
        }

        double legendWidth = legend.PixelEndX - legend.PixelStartX + 1;
        if (legend.EffectiveBoundaries.Count != boundaryCount)
        {
            return legend;
        }

        List<double> expectedBoundaryXs = legend.EffectiveBoundaries
            .Select(boundary => legend.PixelStartX + (boundary.Ratio * legendWidth))
            .ToList();
        List<NumericToken> available = tokens.ToList();
        List<double> values = new(boundaryCount);
        double tolerance = Math.Max(24, legendWidth / Math.Max(2, legend.Bands.Count * 2));
        foreach (double expectedX in expectedBoundaryXs)
        {
            NumericToken? nearest = available
                .OrderBy(token => Math.Abs(token.CenterX - expectedX))
                .FirstOrDefault();
            if (nearest is null || Math.Abs(nearest.CenterX - expectedX) > tolerance)
            {
                return legend;
            }

            values.Add(nearest.Value);
            available.Remove(nearest);
        }

        if (values.Zip(values.Skip(1), (left, right) => left < right).Any(isIncreasing => !isIncreasing))
        {
            return legend;
        }

        IsoFieldLegendBand[] recognizedBands = legend.Bands
            .Select((band, index) => band with
            {
                MinimumValue = values[index],
                MaximumValue = values[index + 1]
            })
            .ToArray();
        IsoFieldLegendBoundary[] recognizedBoundaries = legend.EffectiveBoundaries
            .Select((boundary, index) => boundary with { Value = values[index] })
            .ToArray();
        return legend with
        {
            Bands = recognizedBands,
            Boundaries = recognizedBoundaries
        };
    }

    private static IsoFieldLegend ApplyReinforcementLabels(PixelBuffer buffer, IsoFieldLegend legend)
    {
        if (legend.EffectiveBoundaries.Count != legend.Bands.Count + 1)
        {
            return legend;
        }

        double legendWidth = legend.PixelEndX - legend.PixelStartX + 1;
        IReadOnlyList<ReinforcementLabelMatch>? matches = RecognizeReinforcementLabels(
            buffer,
            legend,
            legendWidth);
        if (matches is null)
        {
            return legend;
        }

        IsoFieldLegendBoundary[] recognizedBoundaries = legend.EffectiveBoundaries
            .Select((boundary, index) => boundary with
            {
                ReinforcementLabel = matches[index].Label,
                LabelConfidence = matches[index].Confidence
            })
            .ToArray();
        IsoFieldLegendBand[] recognizedBands = legend.Bands
            .Select((band, index) => band with { Label = matches[index].Label })
            .ToArray();
        return legend with
        {
            Bands = recognizedBands,
            Boundaries = recognizedBoundaries
        };
    }

    private static IReadOnlyList<ReinforcementLabelMatch>? RecognizeReinforcementLabels(
        PixelBuffer buffer,
        IsoFieldLegend legend,
        double legendWidth)
    {
        int boundaryCount = legend.EffectiveBoundaries.Count;
        if (boundaryCount == 0 || boundaryCount > ReinforcementLabelTemplates.Count)
        {
            return null;
        }

        double[,] scores = new double[boundaryCount, ReinforcementLabelTemplates.Count];
        for (int boundaryIndex = 0; boundaryIndex < boundaryCount; boundaryIndex++)
        {
            IsoFieldLegendBoundary boundary = legend.EffectiveBoundaries[boundaryIndex];
            double expectedCenterX = legend.PixelStartX + (boundary.Ratio * legendWidth);
            for (int templateIndex = 0; templateIndex < ReinforcementLabelTemplates.Count; templateIndex++)
            {
                scores[boundaryIndex, templateIndex] = CalculateBestTemplateCoverage(
                    buffer,
                    ReinforcementLabelTemplates[templateIndex],
                    legend.PixelY,
                    expectedCenterX);
            }
        }

        double[,] totals = new double[boundaryCount, ReinforcementLabelTemplates.Count];
        int[,] parents = new int[boundaryCount, ReinforcementLabelTemplates.Count];
        for (int boundaryIndex = 0; boundaryIndex < boundaryCount; boundaryIndex++)
        {
            for (int templateIndex = 0; templateIndex < ReinforcementLabelTemplates.Count; templateIndex++)
            {
                totals[boundaryIndex, templateIndex] = double.NegativeInfinity;
                parents[boundaryIndex, templateIndex] = -1;
            }
        }

        for (int templateIndex = 0; templateIndex < ReinforcementLabelTemplates.Count; templateIndex++)
        {
            totals[0, templateIndex] = scores[0, templateIndex];
        }

        for (int boundaryIndex = 1; boundaryIndex < boundaryCount; boundaryIndex++)
        {
            for (int templateIndex = boundaryIndex;
                templateIndex < ReinforcementLabelTemplates.Count;
                templateIndex++)
            {
                for (int previousTemplate = boundaryIndex - 1;
                    previousTemplate < templateIndex;
                    previousTemplate++)
                {
                    double total = totals[boundaryIndex - 1, previousTemplate]
                        + scores[boundaryIndex, templateIndex];
                    if (total > totals[boundaryIndex, templateIndex])
                    {
                        totals[boundaryIndex, templateIndex] = total;
                        parents[boundaryIndex, templateIndex] = previousTemplate;
                    }
                }
            }
        }

        int selectedTemplate = Enumerable.Range(0, ReinforcementLabelTemplates.Count)
            .OrderByDescending(index => totals[boundaryCount - 1, index])
            .First();
        int[] selectedTemplates = new int[boundaryCount];
        for (int boundaryIndex = boundaryCount - 1; boundaryIndex >= 0; boundaryIndex--)
        {
            selectedTemplates[boundaryIndex] = selectedTemplate;
            selectedTemplate = parents[boundaryIndex, selectedTemplate];
        }

        List<ReinforcementLabelMatch> matches = new(boundaryCount);
        for (int boundaryIndex = 0; boundaryIndex < boundaryCount; boundaryIndex++)
        {
            int templateIndex = selectedTemplates[boundaryIndex];
            double confidence = scores[boundaryIndex, templateIndex];
            if (confidence < 0.82)
            {
                return null;
            }

            matches.Add(new ReinforcementLabelMatch(
                ReinforcementLabelTemplates[templateIndex].Text,
                confidence));
        }

        return matches;
    }

    private static double CalculateBestTemplateCoverage(
        PixelBuffer buffer,
        ReinforcementLabelTemplate template,
        int legendY,
        double expectedCenterX)
    {
        int nominalLeft = (int)Math.Round(expectedCenterX - (template.Width / 2.0));
        double bestScore = 0;
        for (int top = Math.Max(0, legendY - 20);
            top <= Math.Min(buffer.Height - template.Height, legendY - 7);
            top++)
        {
            for (int left = nominalLeft - 6; left <= nominalLeft + 6; left++)
            {
                bestScore = Math.Max(bestScore, CalculateTemplateCoverage(buffer, template, left, top));
            }
        }

        return bestScore;
    }

    private static double CalculateTemplateCoverage(
        PixelBuffer buffer,
        ReinforcementLabelTemplate template,
        int left,
        int top)
    {
        int matched = 0;
        foreach (PixelPoint point in template.Points)
        {
            int x = left + point.X;
            int y = top + point.Y;
            if (x >= 0
                && x < buffer.Width
                && y >= 0
                && y < buffer.Height
                && IsLegendTextPixel(buffer.GetColor(x, y)))
            {
                matched++;
            }
        }

        return (double)matched / template.Points.Count;
    }

    private static ReinforcementLabelTemplate CreateReinforcementLabelTemplate(string text)
    {
        int[] offsets = text.Length == 7
            ? [0, 7, 14, 20, 26, 34, 41]
            : [0, 7, 14, 20, 26, 34, 41, 48, 55, 62, 69, 75, 81, 89, 96];
        if (offsets.Length != text.Length)
        {
            throw new InvalidOperationException($"Unsupported reinforcement label template: {text}.");
        }

        List<PixelPoint> points = new();
        int width = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            GlyphTemplate glyph = RasterGlyphTemplates.Single(template => template.Character == character);
            int glyphTop = character == 's' ? 3 : character == '+' ? 2 : 0;
            for (int y = 0; y < glyph.Rows.Count; y++)
            {
                for (int x = 0; x < glyph.Rows[y].Length; x++)
                {
                    if (glyph.Rows[y][x] == '#')
                    {
                        points.Add(new PixelPoint(offsets[index] + x, glyphTop + y));
                    }
                }
            }

            width = Math.Max(width, offsets[index] + glyph.Rows[0].Length);
        }

        return new ReinforcementLabelTemplate(text, width, 9, points);
    }

    private static (int top, int bottom)? FindLegendLabelRows(PixelBuffer buffer, IsoFieldLegend legend)
    {
        int legendWidth = legend.PixelEndX - legend.PixelStartX + 1;
        int legendBottom = legend.PixelY;
        int legendSearchBottom = Math.Min(buffer.Height - 1, legend.PixelY + 36);
        for (int y = legend.PixelY; y <= legendSearchBottom; y++)
        {
            int saturatedPixels = 0;
            for (int x = legend.PixelStartX; x <= legend.PixelEndX; x++)
            {
                PixelColor color = buffer.GetColor(x, y);
                int range = Math.Max(color.Red, Math.Max(color.Green, color.Blue))
                    - Math.Min(color.Red, Math.Min(color.Green, color.Blue));
                if (range >= 48)
                {
                    saturatedPixels++;
                }
            }

            if (saturatedPixels >= legendWidth * 0.75)
            {
                legendBottom = y;
            }
        }

        int searchTop = Math.Min(buffer.Height - 1, legendBottom + 2);
        int searchBottom = Math.Min(buffer.Height - 1, legendBottom + 28);
        int maximumInkPerRow = Math.Max(16, legendWidth / 3);
        List<(int top, int bottom, int ink)> groups = new();
        int groupTop = -1;
        int groupInk = 0;
        for (int y = searchTop; y <= searchBottom; y++)
        {
            int ink = 0;
            for (int x = Math.Max(0, legend.PixelStartX - 24);
                x <= Math.Min(buffer.Width - 1, legend.PixelEndX + 24);
                x++)
            {
                if (IsLegendTextPixel(buffer.GetColor(x, y)))
                {
                    ink++;
                }
            }

            bool isTextRow = ink > 0 && ink <= maximumInkPerRow;
            if (isTextRow)
            {
                groupTop = groupTop < 0 ? y : groupTop;
                groupInk += ink;
            }
            else if (groupTop >= 0)
            {
                groups.Add((groupTop, y - 1, groupInk));
                groupTop = -1;
                groupInk = 0;
            }
        }

        if (groupTop >= 0)
        {
            groups.Add((groupTop, searchBottom, groupInk));
        }

        (int top, int bottom, int ink) best = groups
            .Where(group => group.bottom - group.top + 1 >= 6)
            .OrderByDescending(group => group.bottom - group.top + 1)
            .ThenByDescending(group => group.ink)
            .FirstOrDefault();
        return best.bottom >= best.top && best.ink > 0
            ? (best.top, best.bottom)
            : null;
    }

    private static IReadOnlyList<BinaryGlyph> FindBinaryGlyphs(
        PixelBuffer buffer,
        int left,
        int right,
        int top,
        int bottom)
    {
        int width = right - left + 1;
        int height = bottom - top + 1;
        bool[] mask = new bool[width * height];
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                mask[((y - top) * width) + x - left] = IsLegendTextPixel(buffer.GetColor(x, y));
            }
        }

        bool[] visited = new bool[mask.Length];
        Queue<int> queue = new();
        List<BinaryGlyph> glyphs = new();
        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                int start = (offsetY * width) + offsetX;
                if (!mask[start] || visited[start])
                {
                    continue;
                }

                visited[start] = true;
                queue.Enqueue(start);
                List<PixelPoint> points = new();
                int minX = offsetX;
                int maxX = offsetX;
                int minY = offsetY;
                int maxY = offsetY;
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    int pointY = index / width;
                    int pointX = index - (pointY * width);
                    points.Add(new PixelPoint(pointX, pointY));
                    minX = Math.Min(minX, pointX);
                    maxX = Math.Max(maxX, pointX);
                    minY = Math.Min(minY, pointY);
                    maxY = Math.Max(maxY, pointY);
                    for (int deltaY = -1; deltaY <= 1; deltaY++)
                    {
                        int neighborY = pointY + deltaY;
                        if (neighborY < 0 || neighborY >= height)
                        {
                            continue;
                        }

                        for (int deltaX = -1; deltaX <= 1; deltaX++)
                        {
                            if (deltaX == 0 && deltaY == 0)
                            {
                                continue;
                            }

                            int neighborX = pointX + deltaX;
                            if (neighborX < 0 || neighborX >= width)
                            {
                                continue;
                            }

                            int neighbor = (neighborY * width) + neighborX;
                            if (mask[neighbor] && !visited[neighbor])
                            {
                                visited[neighbor] = true;
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }

                BinaryGlyph glyph = new(
                    minX + left,
                    maxX + left,
                    minY + top,
                    maxY + top,
                    points.Select(point => new PixelPoint(point.X - minX, point.Y - minY)).ToArray());
                bool isDigitSize = glyph.Height >= 6 && glyph.Height <= 16 && glyph.Width <= 10;
                bool isDecimalPointSize = glyph.Height <= 3 && glyph.Width <= 3;
                if (isDigitSize || isDecimalPointSize)
                {
                    glyphs.Add(glyph);
                }
            }
        }

        return glyphs.OrderBy(glyph => glyph.MinX).ToArray();
    }

    private static IReadOnlyList<NumericToken> RecognizeNumericTokens(
        IReadOnlyList<BinaryGlyph> glyphs,
        int textBottom)
    {
        if (glyphs.Count == 0)
        {
            return Array.Empty<NumericToken>();
        }

        int maximumInternalGap = Math.Max(5, glyphs.Max(glyph => glyph.Height));
        List<List<BinaryGlyph>> groups = new();
        foreach (BinaryGlyph glyph in glyphs)
        {
            List<BinaryGlyph>? current = groups.LastOrDefault();
            if (current is null || glyph.MinX - current[current.Count - 1].MaxX - 1 > maximumInternalGap)
            {
                current = new List<BinaryGlyph>();
                groups.Add(current);
            }

            current.Add(glyph);
        }

        List<NumericToken> tokens = new();
        foreach (List<BinaryGlyph> group in groups)
        {
            string text = string.Concat(group.Select(glyph => RecognizeGlyph(glyph, textBottom)));
            if (text.Count(character => character == '.') <= 1
                && text.Any(char.IsDigit)
                && double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value))
            {
                tokens.Add(new NumericToken(
                    value,
                    (group[0].MinX + group[group.Count - 1].MaxX) / 2.0));
            }
        }

        return tokens;
    }

    private static char RecognizeGlyph(BinaryGlyph glyph, int textBottom)
    {
        if (glyph.Height <= 3 && glyph.Width <= 3 && glyph.MaxY >= textBottom - 2)
        {
            return '.';
        }

        bool[] candidate = NormalizeGlyph(glyph.Width, glyph.Height, glyph.Points);
        (char character, double score) = NumericGlyphTemplates
            .Select(template => (template.Character, score: CalculateGlyphScore(candidate, template.NormalizedPixels)))
            .OrderByDescending(item => item.score)
            .First();
        return score >= 0.62 ? character : '?';
    }

    private static bool[] NormalizeGlyph(int width, int height, IReadOnlyList<PixelPoint> points)
    {
        bool[] source = new bool[width * height];
        foreach (PixelPoint point in points)
        {
            source[(point.Y * width) + point.X] = true;
        }

        bool[] normalized = new bool[NormalizedGlyphWidth * NormalizedGlyphHeight];
        double scale = Math.Min(
            (double)(NormalizedGlyphWidth - 2) / width,
            (double)(NormalizedGlyphHeight - 2) / height);
        int scaledWidth = Math.Max(1, (int)Math.Round(width * scale));
        int scaledHeight = Math.Max(1, (int)Math.Round(height * scale));
        int offsetX = (NormalizedGlyphWidth - scaledWidth) / 2;
        int offsetY = (NormalizedGlyphHeight - scaledHeight) / 2;
        for (int targetY = 0; targetY < scaledHeight; targetY++)
        {
            int sourceY = Math.Min(height - 1, (int)((targetY + 0.5) * height / scaledHeight));
            for (int targetX = 0; targetX < scaledWidth; targetX++)
            {
                int sourceX = Math.Min(width - 1, (int)((targetX + 0.5) * width / scaledWidth));
                if (source[(sourceY * width) + sourceX])
                {
                    normalized[((targetY + offsetY) * NormalizedGlyphWidth) + targetX + offsetX] = true;
                }
            }
        }

        return normalized;
    }

    private static double CalculateGlyphScore(bool[] left, bool[] right)
    {
        int intersection = 0;
        int ink = 0;
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] && right[index])
            {
                intersection++;
            }

            if (left[index])
            {
                ink++;
            }

            if (right[index])
            {
                ink++;
            }
        }

        return ink == 0 ? 0 : (2.0 * intersection) / ink;
    }

    private static bool IsLegendTextPixel(PixelColor color)
    {
        return color.Alpha > 0
            && color.Red < LegendTextThreshold
            && color.Green < LegendTextThreshold
            && color.Blue < LegendTextThreshold;
    }

    private static string FormatValue(double value)
    {
        return value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static (int StartX, int EndX) FindLongestSaturatedRun(PixelBuffer buffer, int y)
    {
        int bestStart = 0;
        int bestEnd = -1;
        int currentStart = -1;
        for (int x = 0; x < buffer.Width; x++)
        {
            PixelColor color = buffer.GetColor(x, y);
            bool saturated = color.Alpha > 0
                && Math.Max(color.Red, Math.Max(color.Green, color.Blue))
                - Math.Min(color.Red, Math.Min(color.Green, color.Blue)) >= 48;
            if (saturated && currentStart < 0)
            {
                currentStart = x;
            }

            if (!saturated && currentStart >= 0)
            {
                if (x - currentStart > bestEnd - bestStart + 1)
                {
                    bestStart = currentStart;
                    bestEnd = x - 1;
                }

                currentStart = -1;
            }
        }

        if (currentStart >= 0 && buffer.Width - currentStart > bestEnd - bestStart + 1)
        {
            bestStart = currentStart;
            bestEnd = buffer.Width - 1;
        }

        return (bestStart, bestEnd);
    }

    private static int CountStrongColorTransitions(PixelBuffer buffer, int y, int startX, int endX)
    {
        int transitions = 0;
        PixelColor previous = buffer.GetColor(startX, y);
        for (int x = startX + 1; x <= endX; x++)
        {
            PixelColor current = buffer.GetColor(x, y);
            if (ColorDistanceSquared(previous, current) > 34 * 34)
            {
                transitions++;
            }

            previous = current;
        }

        return transitions;
    }

    private static IReadOnlyList<ColorRun> ExtractColorRuns(
        PixelBuffer buffer,
        int y,
        int startX,
        int endX)
    {
        List<ColorRun> runs = new();
        int runStart = startX;
        PixelColor seed = buffer.GetColor(startX, y);
        long redTotal = seed.Red;
        long greenTotal = seed.Green;
        long blueTotal = seed.Blue;
        int count = 1;
        for (int x = startX + 1; x <= endX; x++)
        {
            PixelColor color = buffer.GetColor(x, y);
            PixelColor average = new(
                (byte)(redTotal / count),
                (byte)(greenTotal / count),
                (byte)(blueTotal / count),
                255);
            if (ColorDistanceSquared(color, average) <= 34 * 34)
            {
                redTotal += color.Red;
                greenTotal += color.Green;
                blueTotal += color.Blue;
                count++;
                continue;
            }

            runs.Add(new ColorRun(
                runStart,
                x - 1,
                (byte)(redTotal / count),
                (byte)(greenTotal / count),
                (byte)(blueTotal / count)));
            runStart = x;
            redTotal = color.Red;
            greenTotal = color.Green;
            blueTotal = color.Blue;
            count = 1;
        }

        runs.Add(new ColorRun(
            runStart,
            endX,
            (byte)(redTotal / count),
            (byte)(greenTotal / count),
            (byte)(blueTotal / count)));
        return runs;
    }

    private static ZoneExtractionResult ExtractZones(PixelBuffer buffer, IsoFieldLegend legend)
    {
        int plotTop = Math.Min(buffer.Height - 1, Math.Max(legend.PixelY + 48, (int)(buffer.Height * 0.15)));
        int plotBottom = Math.Max(plotTop, buffer.Height - 12);
        byte[] classes = ClassifyPixels(buffer, legend, plotTop, plotBottom);
        List<IsoFieldPolyline> polylines = new();
        int rejectedComponents = 0;
        int nextZoneId = 1;
        bool[] rawMask = BuildColorMask(classes, buffer.Width, buffer.Height, plotTop, plotBottom);
        bool[] denseMask = BuildDenseMask(rawMask, buffer.Width, buffer.Height, plotTop, plotBottom);
        bool[] expandedMask = Dilate(denseMask, buffer.Width, buffer.Height, plotTop, plotBottom);
        IReadOnlyList<PixelComponent> components = FindComponents(
            expandedMask,
            rawMask,
            buffer.Width,
            buffer.Height,
            plotTop,
            plotBottom);
        foreach (PixelComponent component in components)
        {
            int minimumSupport = Math.Max(14, (buffer.Width * buffer.Height) / 120000);
            bool isOversized = component.Width > buffer.Width * 0.85
                || component.Height > (plotBottom - plotTop + 1) * 0.85;
            if (component.SupportPoints.Count < minimumSupport || isOversized)
            {
                rejectedComponents++;
                continue;
            }

            IReadOnlyList<IsoFieldPoint> hull = BuildConvexHull(component.SupportPoints);
            if (hull.Count < 4)
            {
                rejectedComponents++;
                continue;
            }

            int maximumBandIndex = component.SupportPoints
                .Select(point => Math.Max(0, classes[(point.Y * buffer.Width) + point.X] - 1))
                .Max();
            IsoFieldLegendBand band = legend.Bands[Math.Min(maximumBandIndex, legend.Bands.Count - 1)];
            double density = Math.Min(
                1,
                (double)component.SupportPoints.Count / Math.Max(1, component.Width * component.Height));
            double confidence = Math.Round(Math.Min(0.98, 0.74 + (density * 0.24)), 3);
            polylines.Add(new IsoFieldPolyline(
                $"builtin-zone-{nextZoneId:000}",
                hull,
                BuildZoneName(band),
                confidence));
            nextZoneId++;
        }

        return new ZoneExtractionResult(polylines, rejectedComponents);
    }

    private static string BuildZoneName(IsoFieldLegendBand band)
    {
        return band.MinimumValue.HasValue && band.MaximumValue.HasValue
            ? $"{FormatValue(band.MinimumValue.Value)}–{FormatValue(band.MaximumValue.Value)} см²/м · {band.HexColor}"
            : $"Макс. уровень {band.Index + 1} · {band.HexColor}";
    }

    private static byte[] ClassifyPixels(
        PixelBuffer buffer,
        IsoFieldLegend legend,
        int plotTop,
        int plotBottom)
    {
        byte[] classes = new byte[buffer.Width * buffer.Height];
        int maximumDistance = ColorDistanceThreshold * ColorDistanceThreshold;
        for (int y = plotTop; y <= plotBottom; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                PixelColor color = buffer.GetColor(x, y);
                int colorRange = Math.Max(color.Red, Math.Max(color.Green, color.Blue))
                    - Math.Min(color.Red, Math.Min(color.Green, color.Blue));
                if (color.Alpha == 0 || colorRange < 48)
                {
                    continue;
                }

                int bestBand = -1;
                int bestDistance = int.MaxValue;
                foreach (IsoFieldLegendBand band in legend.Bands)
                {
                    int distance = ColorDistanceSquared(
                        color,
                        new PixelColor(band.Red, band.Green, band.Blue, 255));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestBand = band.Index;
                    }
                }

                if (bestBand >= 0 && bestDistance <= maximumDistance)
                {
                    classes[(y * buffer.Width) + x] = (byte)(bestBand + 1);
                }
            }
        }

        return classes;
    }

    private static bool[] BuildColorMask(
        byte[] classes,
        int width,
        int height,
        int plotTop,
        int plotBottom)
    {
        bool[] mask = new bool[width * height];
        for (int y = plotTop; y <= plotBottom; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                mask[row + x] = classes[row + x] > 0;
            }
        }

        return mask;
    }

    private static bool[] BuildDenseMask(
        bool[] rawMask,
        int width,
        int height,
        int plotTop,
        int plotBottom)
    {
        int[] integral = BuildIntegralImage(rawMask, width, height);
        bool[] denseMask = new bool[width * height];
        for (int y = plotTop; y <= plotBottom; y++)
        {
            int top = Math.Max(plotTop, y - DensityRadius);
            int bottom = Math.Min(plotBottom, y + DensityRadius);
            for (int x = 0; x < width; x++)
            {
                int left = Math.Max(0, x - DensityRadius);
                int right = Math.Min(width - 1, x + DensityRadius);
                int count = CountRectangle(integral, width, left, top, right - left + 1, bottom - top + 1);
                denseMask[(y * width) + x] = count >= MinimumDensePixels;
            }
        }

        return denseMask;
    }

    private static bool[] Dilate(
        bool[] source,
        int width,
        int height,
        int plotTop,
        int plotBottom)
    {
        bool[] result = new bool[width * height];
        for (int y = plotTop; y <= plotBottom; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!source[(y * width) + x])
                {
                    continue;
                }

                for (int offsetY = -DilationRadius; offsetY <= DilationRadius; offsetY++)
                {
                    int targetY = y + offsetY;
                    if (targetY < plotTop || targetY > plotBottom)
                    {
                        continue;
                    }

                    int targetRow = targetY * width;
                    for (int offsetX = -DilationRadius; offsetX <= DilationRadius; offsetX++)
                    {
                        int targetX = x + offsetX;
                        if (targetX >= 0 && targetX < width)
                        {
                            result[targetRow + targetX] = true;
                        }
                    }
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<PixelComponent> FindComponents(
        bool[] mask,
        bool[] supportMask,
        int width,
        int height,
        int plotTop,
        int plotBottom)
    {
        bool[] visited = new bool[width * height];
        List<PixelComponent> components = new();
        Queue<int> queue = new();
        for (int y = plotTop; y <= plotBottom; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int start = (y * width) + x;
                if (!mask[start] || visited[start])
                {
                    continue;
                }

                visited[start] = true;
                queue.Enqueue(start);
                int minX = x;
                int maxX = x;
                int minY = y;
                int maxY = y;
                List<PixelPoint> supportPoints = new();
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    int pointY = index / width;
                    int pointX = index - (pointY * width);
                    minX = Math.Min(minX, pointX);
                    maxX = Math.Max(maxX, pointX);
                    minY = Math.Min(minY, pointY);
                    maxY = Math.Max(maxY, pointY);
                    if (supportMask[index])
                    {
                        supportPoints.Add(new PixelPoint(pointX, pointY));
                    }

                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        int neighborY = pointY + offsetY;
                        if (neighborY < plotTop || neighborY > plotBottom)
                        {
                            continue;
                        }

                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            if (offsetX == 0 && offsetY == 0)
                            {
                                continue;
                            }

                            int neighborX = pointX + offsetX;
                            if (neighborX < 0 || neighborX >= width)
                            {
                                continue;
                            }

                            int neighbor = (neighborY * width) + neighborX;
                            if (mask[neighbor] && !visited[neighbor])
                            {
                                visited[neighbor] = true;
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }

                components.Add(new PixelComponent(minX, maxX, minY, maxY, supportPoints));
            }
        }

        return components;
    }

    private static IReadOnlyList<IsoFieldPoint> BuildConvexHull(IReadOnlyList<PixelPoint> sourcePoints)
    {
        PixelPoint[] points = sourcePoints
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToArray();
        if (points.Length < 3)
        {
            return Array.Empty<IsoFieldPoint>();
        }

        List<PixelPoint> hull = new();
        foreach (PixelPoint point in points)
        {
            while (hull.Count >= 2
                && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        int lowerCount = hull.Count;
        for (int index = points.Length - 2; index >= 0; index--)
        {
            PixelPoint point = points[index];
            while (hull.Count > lowerCount
                && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        hull.RemoveAt(hull.Count - 1);
        List<IsoFieldPoint> result = hull
            .Select(point => new IsoFieldPoint(point.X, point.Y))
            .ToList();
        result.Add(result[0]);
        return result;
    }

    private static long Cross(PixelPoint origin, PixelPoint a, PixelPoint b)
    {
        return ((long)a.X - origin.X) * ((long)b.Y - origin.Y)
            - ((long)a.Y - origin.Y) * ((long)b.X - origin.X);
    }

    private static int[] BuildIntegralImage(bool[] source, int width, int height)
    {
        int integralWidth = width + 1;
        int[] integral = new int[integralWidth * (height + 1)];
        for (int y = 0; y < height; y++)
        {
            int rowTotal = 0;
            for (int x = 0; x < width; x++)
            {
                if (source[(y * width) + x])
                {
                    rowTotal++;
                }

                integral[((y + 1) * integralWidth) + x + 1] =
                    integral[(y * integralWidth) + x + 1] + rowTotal;
            }
        }

        return integral;
    }

    private static int CountRectangle(
        int[] integral,
        int sourceWidth,
        int x,
        int y,
        int width,
        int height)
    {
        int integralWidth = sourceWidth + 1;
        int right = x + width;
        int bottom = y + height;
        return integral[(bottom * integralWidth) + right]
            - integral[(y * integralWidth) + right]
            - integral[(bottom * integralWidth) + x]
            + integral[(y * integralWidth) + x];
    }

    private static int ColorDistanceSquared(PixelColor left, PixelColor right)
    {
        int red = left.Red - right.Red;
        int green = left.Green - right.Green;
        int blue = left.Blue - right.Blue;
        return (red * red) + (green * green) + (blue * blue);
    }

    private sealed record ColorRun(
        int StartX,
        int EndX,
        byte Red,
        byte Green,
        byte Blue)
    {
        public int Length => EndX - StartX + 1;
    }

    private sealed record ZoneExtractionResult(
        IReadOnlyList<IsoFieldPolyline> Polylines,
        int RejectedComponents);

    private sealed record NumericToken(double Value, double CenterX);

    private sealed record ReinforcementLabelMatch(string Label, double Confidence);

    private sealed record ReinforcementLabelTemplate(
        string Text,
        int Width,
        int Height,
        IReadOnlyList<PixelPoint> Points);

    private sealed record BinaryGlyph(
        int MinX,
        int MaxX,
        int MinY,
        int MaxY,
        IReadOnlyList<PixelPoint> Points)
    {
        public int Width => MaxX - MinX + 1;

        public int Height => MaxY - MinY + 1;
    }

    private sealed record GlyphTemplate(char Character, IReadOnlyList<string> Rows)
    {
        public bool[] NormalizedPixels { get; } = NormalizeGlyph(
            Rows[0].Length,
            Rows.Count,
            Rows.SelectMany((row, y) => row.Select((pixel, x) => (pixel, point: new PixelPoint(x, y))))
                .Where(item => item.pixel == '#')
                .Select(item => item.point)
                .ToArray());
    }

    private sealed record PixelComponent(
        int MinX,
        int MaxX,
        int MinY,
        int MaxY,
        IReadOnlyList<PixelPoint> SupportPoints)
    {
        public int Width => MaxX - MinX + 1;

        public int Height => MaxY - MinY + 1;
    }

    private readonly record struct PixelPoint(int X, int Y);

    private readonly record struct PixelColor(byte Red, byte Green, byte Blue, byte Alpha);

    private sealed class PixelBuffer
    {
        private PixelBuffer(int width, int height, byte[] pixels, int stride)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
            Stride = stride;
        }

        public int Width { get; }

        public int Height { get; }

        private byte[] Pixels { get; }

        private int Stride { get; }

        public static PixelBuffer Create(BitmapSource source)
        {
            FormatConvertedBitmap converted = new(source, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return new PixelBuffer(converted.PixelWidth, converted.PixelHeight, pixels, stride);
        }

        public PixelColor GetColor(int x, int y)
        {
            int index = (y * Stride) + (x * 4);
            return new PixelColor(
                Pixels[index + 2],
                Pixels[index + 1],
                Pixels[index],
                Pixels[index + 3]);
        }
    }
}
