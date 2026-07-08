using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;

public sealed class MepDimensionCollectorService
{
    private const string HorizontalDirection = "Горизонтальные трассы";
    private const string VerticalDirection = "Вертикальные трассы";
    private static readonly IReadOnlyList<CategoryDefinition> CategoryDefinitions =
    [
        new(MepDimensionCategoryKeys.Pipes, "Трубы", BuiltInCategory.OST_PipeCurves),
        new(MepDimensionCategoryKeys.Ducts, "Воздуховоды", BuiltInCategory.OST_DuctCurves),
        new(MepDimensionCategoryKeys.CableTrays, "Лотки", BuiltInCategory.OST_CableTray),
        new(MepDimensionCategoryKeys.Conduits, "Кабель-каналы", BuiltInCategory.OST_Conduit)
    ];

    public static bool CanUseActiveView(View? activeView, out string message)
    {
        if (activeView is null)
        {
            message = "Активный вид не найден.";
            return false;
        }

        if (activeView.IsTemplate)
        {
            message = "Авторазмеры MEP работают только на обычном активном виде, а не на шаблоне вида.";
            return false;
        }

        if (activeView.ViewType is ViewType.ThreeD
            or ViewType.DrawingSheet
            or ViewType.ProjectBrowser
            or ViewType.SystemBrowser
            or ViewType.Internal
            or ViewType.Schedule
            or ViewType.Report
            or ViewType.Legend)
        {
            message = "Откройте 2D-вид модели: план, разрез, фасад или потолочный план.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public IReadOnlyList<MepDimensionCandidate> Collect(
        Document document,
        View activeView,
        MepDimensionProfile profile)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(profile, nameof(profile));

        ViewFrame frame = ViewFrame.Create(activeView);
        List<MepLineInfo> lines = [];
        foreach (CategoryDefinition category in GetEnabledCategories(profile))
        {
            lines.AddRange(CollectLines(document, activeView, frame, category, profile));
        }

        List<MepDimensionCandidate> candidates = [];
        foreach (IGrouping<(string CategoryKey, bool IsHorizontal), MepLineInfo> group in lines
            .GroupBy(line => (line.CategoryKey, line.IsHorizontal)))
        {
            MepLineInfo first = group.First();
            MepDimensionCandidate? candidate = CreateCandidate(first.CategoryName, group.Key.IsHorizontal, group.ToList(), frame);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates
            .OrderBy(candidate => candidate.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.DirectionName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<MepLineInfo> CollectLines(
        Document document,
        View activeView,
        ViewFrame frame,
        CategoryDefinition category,
        MepDimensionProfile profile)
    {
        long categoryId = (long)(int)category.BuiltInCategory;
        foreach (Element element in new FilteredElementCollector(document, activeView.Id).WhereElementIsNotElementType())
        {
            if (element.Category is null || RevitElementIds.GetValue(element.Category.Id) != categoryId)
            {
                continue;
            }

            if (element.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line line)
            {
                continue;
            }

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            if (!TryClassifyLine(start, end, frame, profile.AngleToleranceDegrees, out bool isHorizontal))
            {
                continue;
            }

            double startAlong = isHorizontal ? frame.ToX(start) : frame.ToY(start);
            double endAlong = isHorizontal ? frame.ToX(end) : frame.ToY(end);
            double offset = isHorizontal
                ? (frame.ToY(start) + frame.ToY(end)) * 0.5
                : (frame.ToX(start) + frame.ToX(end)) * 0.5;
            bool hasReference = MepDimensionReferenceResolver.TryResolve(element, activeView, profile.AllowElementReferenceFallback, out _, out _);

            yield return new MepLineInfo(
                RevitElementIds.GetValue(element.Id),
                category.Key,
                category.DisplayName,
                isHorizontal,
                Math.Min(startAlong, endAlong),
                Math.Max(startAlong, endAlong),
                offset,
                frame.ToNormal((start + end) * 0.5),
                hasReference);
        }
    }

    private static MepDimensionCandidate? CreateCandidate(
        string categoryName,
        bool isHorizontal,
        IReadOnlyList<MepLineInfo> lines,
        ViewFrame frame)
    {
        List<MepLineInfo> readyLines = lines.Where(line => line.HasReference).OrderBy(line => line.Offset).ToList();
        int missingReferenceCount = lines.Count - readyLines.Count;
        if (readyLines.Count < 2)
        {
            return new MepDimensionCandidate(
                CreateCandidateId(categoryName, isHorizontal),
                categoryName,
                isHorizontal ? HorizontalDirection : VerticalDirection,
                isHorizontal,
                readyLines.Select(line => line.ElementId).ToList(),
                lines.Count,
                readyLines.Count,
                missingReferenceCount,
                XYZ.Zero,
                XYZ.Zero,
                "Нужно минимум два элемента с доступными геометрическими Reference.");
        }

        double commonStart = readyLines.Max(line => line.StartAlong);
        double commonEnd = readyLines.Min(line => line.EndAlong);
        if (commonEnd < commonStart)
        {
            return new MepDimensionCandidate(
                CreateCandidateId(categoryName, isHorizontal),
                categoryName,
                isHorizontal ? HorizontalDirection : VerticalDirection,
                isHorizontal,
                readyLines.Select(line => line.ElementId).ToList(),
                lines.Count,
                readyLines.Count,
                missingReferenceCount,
                XYZ.Zero,
                XYZ.Zero,
                "У параллельных трасс нет общего участка для размерной линии.");
        }

        double along = (commonStart + commonEnd) * 0.5;
        double minOffset = readyLines.Min(line => line.Offset);
        double maxOffset = readyLines.Max(line => line.Offset);
        double normal = readyLines.Average(line => line.Normal);
        XYZ dimensionStart = isHorizontal
            ? frame.FromCoordinates(along, minOffset, normal)
            : frame.FromCoordinates(minOffset, along, normal);
        XYZ dimensionEnd = isHorizontal
            ? frame.FromCoordinates(along, maxOffset, normal)
            : frame.FromCoordinates(maxOffset, along, normal);
        string message = missingReferenceCount > 0
            ? $"Готово. Без Reference: {missingReferenceCount}."
            : "Готово к созданию размерной цепочки.";

        return new MepDimensionCandidate(
            CreateCandidateId(categoryName, isHorizontal),
            categoryName,
            isHorizontal ? HorizontalDirection : VerticalDirection,
            isHorizontal,
            readyLines.Select(line => line.ElementId).ToList(),
            lines.Count,
            readyLines.Count,
            missingReferenceCount,
            dimensionStart,
            dimensionEnd,
            message);
    }

    private static bool TryClassifyLine(
        XYZ start,
        XYZ end,
        ViewFrame frame,
        double angleToleranceDegrees,
        out bool isHorizontal)
    {
        double dx = frame.ToX(end) - frame.ToX(start);
        double dy = frame.ToY(end) - frame.ToY(start);
        double absDx = Math.Abs(dx);
        double absDy = Math.Abs(dy);
        double length = Math.Sqrt(absDx * absDx + absDy * absDy);
        if (length < 1e-6)
        {
            isHorizontal = true;
            return false;
        }

        double angle = Math.Atan2(Math.Min(absDx, absDy), Math.Max(absDx, absDy)) * 180.0 / Math.PI;
        if (angle > angleToleranceDegrees)
        {
            isHorizontal = true;
            return false;
        }

        isHorizontal = absDx >= absDy;
        return true;
    }

    private static string CreateCandidateId(string categoryName, bool isHorizontal)
    {
        return $"{categoryName}-{(isHorizontal ? "H" : "V")}";
    }

    private static IEnumerable<CategoryDefinition> GetEnabledCategories(MepDimensionProfile profile)
    {
        foreach (CategoryDefinition category in CategoryDefinitions)
        {
            if (category.Key == MepDimensionCategoryKeys.Pipes && profile.IncludePipes
                || category.Key == MepDimensionCategoryKeys.Ducts && profile.IncludeDucts
                || category.Key == MepDimensionCategoryKeys.CableTrays && profile.IncludeCableTrays
                || category.Key == MepDimensionCategoryKeys.Conduits && profile.IncludeConduits)
            {
                yield return category;
            }
        }
    }

    private sealed record CategoryDefinition(string Key, string DisplayName, BuiltInCategory BuiltInCategory);

    private sealed record MepLineInfo(
        long ElementId,
        string CategoryKey,
        string CategoryName,
        bool IsHorizontal,
        double StartAlong,
        double EndAlong,
        double Offset,
        double Normal,
        bool HasReference);
}
