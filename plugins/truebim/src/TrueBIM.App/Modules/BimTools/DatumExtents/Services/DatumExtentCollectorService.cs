using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Services;

public sealed class DatumExtentCollectorService
{
    public static bool CanUseActiveView(View? activeView, out string message)
    {
        if (activeView is null)
        {
            message = "Активный вид не найден.";
            return false;
        }

        if (activeView.IsTemplate)
        {
            message = "Оси 2D/3D работают только на обычном активном виде, а не на шаблоне вида.";
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

    public IReadOnlyList<DatumExtentRow> Collect(
        Document document,
        View activeView,
        DatumExtentProfile profile)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(profile, nameof(profile));

        DatumExtentType targetType = DatumExtentTargets.ToRevitType(profile.TargetExtentType);
        Dictionary<long, DatumPlane> datums = [];

        if (profile.IncludeGrids)
        {
            foreach (Grid grid in CollectVisibleGrids(document, activeView))
            {
                datums[RevitElementIds.GetValue(grid.Id)] = grid;
            }
        }

        if (profile.IncludeLevels)
        {
            foreach (Level level in new FilteredElementCollector(document, activeView.Id).OfClass(typeof(Level)).Cast<Level>())
            {
                datums[RevitElementIds.GetValue(level.Id)] = level;
            }
        }

        return datums.Values
            .OrderBy(GetDatumKind, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(datum => datum.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(datum => CreateRow(datum, activeView, targetType, profile.IncludeEnd0, profile.IncludeEnd1, profile.PropagateToViews))
            .ToList();
    }

    public static IReadOnlyList<Grid> CollectVisibleGrids(Document document, View activeView)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));

        return new FilteredElementCollector(document, activeView.Id)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .OrderBy(grid => grid.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DatumExtentRow CreateRow(
        DatumPlane datum,
        View activeView,
        DatumExtentType targetType,
        bool includeEnd0,
        bool includeEnd1,
        bool propagateToViews)
    {
        bool canReadEnd0 = TryGetExtentType(datum, activeView, DatumEnds.End0, out DatumExtentType end0Type, out string end0Message);
        bool canReadEnd1 = TryGetExtentType(datum, activeView, DatumEnds.End1, out DatumExtentType end1Type, out string end1Message);
        int modelCurveCount = GetCurveCount(datum, activeView, DatumExtentType.Model);
        int viewSpecificCurveCount = GetCurveCount(datum, activeView, DatumExtentType.ViewSpecific);
        int propagationViewCount = GetPropagationViewCount(datum, activeView);

        List<string> messages = [];
        bool needsEnd0 = includeEnd0 && canReadEnd0 && end0Type != targetType;
        bool needsEnd1 = includeEnd1 && canReadEnd1 && end1Type != targetType;
        bool hasReadableEnd = (includeEnd0 && canReadEnd0) || (includeEnd1 && canReadEnd1);
        bool canPropagate = propagateToViews && propagationViewCount > 0;
        string status = DatumExtentStatuses.Ready;
        bool canApply = needsEnd0 || needsEnd1 || canPropagate;

        if (!hasReadableEnd)
        {
            status = DatumExtentStatuses.Skipped;
            canApply = false;
            messages.Add("Не удалось прочитать выбранные концы datum-элемента.");
        }
        else if (!canApply)
        {
            status = DatumExtentStatuses.Unchanged;
            messages.Add("Выбранные концы уже в целевом режиме.");
        }
        else if (!needsEnd0 && !needsEnd1 && canPropagate)
        {
            messages.Add("Готово к распространению текущих экстентов на совместимые виды.");
        }
        else
        {
            messages.Add("Готово к переключению выбранных концов.");
        }

        if (propagateToViews)
        {
            messages.Add($"Совместимых видов для распространения: {propagationViewCount}.");
        }

        if (!canReadEnd0 && includeEnd0)
        {
            messages.Add($"End0: {end0Message}");
        }

        if (!canReadEnd1 && includeEnd1)
        {
            messages.Add($"End1: {end1Message}");
        }

        return new DatumExtentRow(
            RevitElementIds.GetValue(datum.Id),
            GetDatumKind(datum),
            datum.Name,
            canReadEnd0 ? DatumExtentTargets.GetDisplayName(end0Type) : "Недоступно",
            canReadEnd1 ? DatumExtentTargets.GetDisplayName(end1Type) : "Недоступно",
            modelCurveCount,
            viewSpecificCurveCount,
            propagationViewCount,
            status,
            string.Join(" ", messages),
            canApply);
    }

    private static bool TryGetExtentType(
        DatumPlane datum,
        View activeView,
        DatumEnds end,
        out DatumExtentType extentType,
        out string message)
    {
        try
        {
            extentType = datum.GetDatumExtentTypeInView(end, activeView);
            message = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            extentType = DatumExtentType.Model;
            message = exception.Message;
            return false;
        }
    }

    private static int GetCurveCount(DatumPlane datum, View activeView, DatumExtentType extentType)
    {
        try
        {
            return datum.GetCurvesInView(extentType, activeView).Count;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int GetPropagationViewCount(DatumPlane datum, View activeView)
    {
        try
        {
            return datum.GetPropagationViews(activeView).Count;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string GetDatumKind(DatumPlane datum)
    {
        return datum switch
        {
            Grid => "Ось",
            Level => "Уровень",
            _ => "Datum"
        };
    }
}
