using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Services;

public sealed class DatumExtentService
{
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

    private static RowApplyState ApplyEnds(
        DatumPlane datum,
        View activeView,
        DatumExtentType targetType,
        DatumExtentProfile profile)
    {
        int changed = 0;
        int unchanged = 0;
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
                $"Выбранные концы уже в целевом режиме. Без изменений: {unchanged}.");
        }

        return new RowApplyState(
            DatumExtentTargets.GetDisplayName(end0Type),
            DatumExtentTargets.GetDisplayName(end1Type),
            DatumExtentStatuses.Done,
            $"Изменено концов: {changed}. Без изменений: {unchanged}.");
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

    private sealed record RowApplyState(
        string End0Type,
        string End1Type,
        string Status,
        string Message);
}
