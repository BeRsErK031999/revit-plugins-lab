using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Services;

public sealed class DatumExtentService
{
    public DatumExtentApplyResult ApplyToVisibleGrids(
        Document document,
        View activeView,
        DatumExtentMode mode,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(logger, nameof(logger));

        IReadOnlyList<Grid> grids = DatumExtentCollectorService.CollectVisibleGrids(document, activeView);
        List<DatumExtentReportRow> reportRows = [];
        if (grids.Count == 0)
        {
            return new DatumExtentApplyResult(reportRows);
        }

        using Transaction transaction = new(document, "TrueBIM Grid Extent Mode");
        transaction.Start();

        foreach (Grid grid in grids)
        {
            try
            {
                RowApplyState state = ApplyAllGridEnds(grid, activeView, mode);
                reportRows.Add(CreateGridReportRow(
                    activeView,
                    grid,
                    mode,
                    state.End0Type,
                    state.End1Type,
                    state.Status,
                    state.Message));
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to apply grid extents for element {RevitElementIds.GetValue(grid.Id)}.", exception);
                reportRows.Add(CreateGridReportRow(
                    activeView,
                    grid,
                    mode,
                    "Недоступно",
                    "Недоступно",
                    DatumExtentStatuses.Error,
                    exception.Message));
            }
        }

        transaction.Commit();
        return new DatumExtentApplyResult(reportRows);
    }

    public DatumExtentApplyResult Apply(
        Document document,
        View activeView,
        IReadOnlyList<DatumExtentRow> rows,
        DatumExtentProfile profile,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(rows, nameof(rows));
        Guard.NotNull(profile, nameof(profile));
        Guard.NotNull(logger, nameof(logger));

        DatumExtentType targetType = DatumExtentTargets.ToRevitType(profile.TargetExtentType);
        List<DatumExtentReportRow> reportRows = [];
        using Transaction transaction = new(document, "TrueBIM Datum Extents");
        transaction.Start();

        foreach (DatumExtentRow row in rows.Where(row => row.IsSelected && row.CanApply))
        {
            try
            {
                if (document.GetElement(RevitElementIds.Create(row.ElementId)) is not DatumPlane datum)
                {
                    reportRows.Add(CreateReportRow(activeView, row, profile, row.End0Type, row.End1Type, DatumExtentStatuses.Error, "Datum-элемент не найден."));
                    continue;
                }

                RowApplyState state = ApplyEnds(datum, activeView, targetType, profile);
                reportRows.Add(CreateReportRow(
                    activeView,
                    row,
                    profile,
                    state.End0Type,
                    state.End1Type,
                    state.Status,
                    state.Message));
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to apply datum extents for element {row.ElementId}.", exception);
                reportRows.Add(CreateReportRow(activeView, row, profile, row.End0Type, row.End1Type, DatumExtentStatuses.Error, exception.Message));
            }
        }

        transaction.Commit();
        return new DatumExtentApplyResult(reportRows);
    }

    private static RowApplyState ApplyAllGridEnds(
        DatumPlane datum,
        View activeView,
        DatumExtentMode mode)
    {
        int changed = 0;
        int unchanged = 0;
        List<string> failures = [];

        DatumExtentType end0Type = ApplyEnd(datum, activeView, DatumEnds.End0, mode, ref changed, ref unchanged, failures);
        DatumExtentType end1Type = ApplyEnd(datum, activeView, DatumEnds.End1, mode, ref changed, ref unchanged, failures);

        if (failures.Count > 0)
        {
            return new RowApplyState(
                DatumExtentTargets.GetDisplayName(end0Type),
                DatumExtentTargets.GetDisplayName(end1Type),
                DatumExtentStatuses.Error,
                $"Изменено концов: {changed}. Ошибки: {string.Join(" ", failures)}");
        }

        if (changed == 0)
        {
            return new RowApplyState(
                DatumExtentTargets.GetDisplayName(end0Type),
                DatumExtentTargets.GetDisplayName(end1Type),
                DatumExtentStatuses.Unchanged,
                $"Ось уже находится в режиме «{DatumExtentModes.GetDisplayName(mode)}». Без изменений: {unchanged}.");
        }

        return new RowApplyState(
            DatumExtentTargets.GetDisplayName(end0Type),
            DatumExtentTargets.GetDisplayName(end1Type),
            DatumExtentStatuses.Done,
            $"Изменено концов: {changed}. Без изменений: {unchanged}.");
    }

    private static DatumExtentType ApplyEnd(
        DatumPlane datum,
        View activeView,
        DatumEnds end,
        DatumExtentMode mode,
        ref int changed,
        ref int unchanged,
        List<string> failures)
    {
        try
        {
            DatumExtentType currentType = datum.GetDatumExtentTypeInView(end, activeView);
            DatumExtentType targetType = DatumExtentModes.GetTargetType(mode, currentType);
            if (currentType == targetType)
            {
                unchanged++;
                return currentType;
            }

            datum.SetDatumExtentType(end, activeView, targetType);
            changed++;
            return datum.GetDatumExtentTypeInView(end, activeView);
        }
        catch (Exception exception)
        {
            failures.Add($"{end}: {exception.Message}");
            return DatumExtentType.Model;
        }
    }

    private static RowApplyState ApplyEnds(
        DatumPlane datum,
        View activeView,
        DatumExtentType targetType,
        DatumExtentProfile profile)
    {
        int changed = 0;
        int unchanged = 0;
        int propagatedViews = 0;
        List<string> failures = [];

        DatumExtentType end0Type = GetCurrentType(datum, activeView, DatumEnds.End0);
        DatumExtentType end1Type = GetCurrentType(datum, activeView, DatumEnds.End1);

        if (profile.IncludeEnd0)
        {
            ApplyEnd(datum, activeView, DatumEnds.End0, targetType, ref end0Type, ref changed, ref unchanged, failures);
        }

        if (profile.IncludeEnd1)
        {
            ApplyEnd(datum, activeView, DatumEnds.End1, targetType, ref end1Type, ref changed, ref unchanged, failures);
        }

        if (profile.PropagateToViews && failures.Count == 0)
        {
            propagatedViews = PropagateToCompatibleViews(datum, activeView, failures);
        }

        if (failures.Count > 0)
        {
            return new RowApplyState(
                DatumExtentTargets.GetDisplayName(end0Type),
                DatumExtentTargets.GetDisplayName(end1Type),
                DatumExtentStatuses.Error,
                $"Изменено концов: {changed}. Распространено видов: {propagatedViews}. Ошибки: {string.Join(" ", failures)}");
        }

        if (changed == 0 && propagatedViews == 0)
        {
            string unchangedMessage = profile.PropagateToViews
                ? $"Выбранные концы уже в целевом режиме. Без изменений: {unchanged}. Совместимых видов для распространения нет."
                : $"Выбранные концы уже в целевом режиме. Без изменений: {unchanged}.";

            return new RowApplyState(
                DatumExtentTargets.GetDisplayName(end0Type),
                DatumExtentTargets.GetDisplayName(end1Type),
                DatumExtentStatuses.Unchanged,
                unchangedMessage);
        }

        string propagationMessage = profile.PropagateToViews
            ? $" Распространено видов: {propagatedViews}."
            : string.Empty;

        return new RowApplyState(
            DatumExtentTargets.GetDisplayName(end0Type),
            DatumExtentTargets.GetDisplayName(end1Type),
            DatumExtentStatuses.Done,
            $"Изменено концов: {changed}. Без изменений: {unchanged}.{propagationMessage}");
    }

    private static void ApplyEnd(
        DatumPlane datum,
        View activeView,
        DatumEnds end,
        DatumExtentType targetType,
        ref DatumExtentType currentType,
        ref int changed,
        ref int unchanged,
        List<string> failures)
    {
        try
        {
            currentType = datum.GetDatumExtentTypeInView(end, activeView);
            if (currentType == targetType)
            {
                unchanged++;
                return;
            }

            datum.SetDatumExtentType(end, activeView, targetType);
            currentType = datum.GetDatumExtentTypeInView(end, activeView);
            changed++;
        }
        catch (Exception exception)
        {
            failures.Add($"{end}: {exception.Message}");
        }
    }

    private static DatumExtentType GetCurrentType(DatumPlane datum, View activeView, DatumEnds end)
    {
        try
        {
            return datum.GetDatumExtentTypeInView(end, activeView);
        }
        catch (Exception)
        {
            return DatumExtentType.Model;
        }
    }

    private static int PropagateToCompatibleViews(DatumPlane datum, View activeView, List<string> failures)
    {
        try
        {
            ISet<ElementId> targetViewIds = datum.GetPropagationViews(activeView);
            if (targetViewIds.Count == 0)
            {
                return 0;
            }

            datum.PropagateToViews(activeView, targetViewIds);
            return targetViewIds.Count;
        }
        catch (Exception exception)
        {
            failures.Add($"Распространение: {exception.Message}");
            return 0;
        }
    }

    private static DatumExtentReportRow CreateReportRow(
        View activeView,
        DatumExtentRow row,
        DatumExtentProfile profile,
        string end0Type,
        string end1Type,
        string status,
        string message)
    {
        return new DatumExtentReportRow(
            "Применение",
            activeView.Name,
            row.ElementId,
            row.Kind,
            row.Name,
            DatumExtentTargets.GetDisplayName(profile.TargetExtentType),
            end0Type,
            end1Type,
            status,
            message);
    }

    private static DatumExtentReportRow CreateGridReportRow(
        View activeView,
        Grid grid,
        DatumExtentMode mode,
        string end0Type,
        string end1Type,
        string status,
        string message)
    {
        return new DatumExtentReportRow(
            "Применение",
            activeView.Name,
            RevitElementIds.GetValue(grid.Id),
            "Ось",
            grid.Name,
            DatumExtentModes.GetDisplayName(mode),
            end0Type,
            end1Type,
            status,
            message);
    }

    private sealed record RowApplyState(
        string End0Type,
        string End1Type,
        string Status,
        string Message);
}
