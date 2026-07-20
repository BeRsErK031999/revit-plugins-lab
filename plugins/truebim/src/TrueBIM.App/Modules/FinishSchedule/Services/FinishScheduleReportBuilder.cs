using System.Globalization;
using System.Text;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishScheduleReportBuilder
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string BuildPreview(
        FinishSchedulePreviewResult preview,
        FinishScheduleSettings settings)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        StringBuilder report = Header("ПРЕДПРОСМОТР");
        AppendCalculation(report, preview, settings);
        AppendGuidance(report, FinishScheduleDiagnosticGuidanceBuilder.Build(preview));
        AppendWarnings(report, ExcludeDiagnosticMessages(preview.Warnings, preview.GeometryWarnings));
        AppendGeometryDiagnostics(report, preview.GeometryWarnings);
        return report.ToString().TrimEnd();
    }

    public string BuildWritePreview(FinishScheduleWritePreview preview)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        StringBuilder report = Header("ПЛАН ЗАПИСИ");
        if (preview.Calculation is not null)
        {
            AppendCalculation(report, preview.Calculation, settings: null);
            AppendGuidance(
                report,
                FinishScheduleDiagnosticGuidanceBuilder.Build(preview.Calculation));
        }

        AppendWritePlan(report, preview);
        AppendWarnings(
            report,
            ExcludeDiagnosticMessages(
                preview.CalculationWarnings.Concat(preview.Issues.Select(issue => issue.Message)),
                preview.Calculation?.GeometryWarnings ?? []));
        AppendGeometryDiagnostics(report, preview.Calculation?.GeometryWarnings ?? []);
        return report.ToString().TrimEnd();
    }

    public string BuildResult(
        FinishScheduleWritePreview preview,
        FinishScheduleWriteResult result)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        bool incompleteCalculation = preview.Calculation is not null
            && FinishGeometryWarningClassifier.HasIncompleteScheduleValues(preview.Calculation);
        StringBuilder report = Header(FormatStatus(result.Status, incompleteCalculation));
        if (preview.Calculation is not null)
        {
            AppendCalculation(report, preview.Calculation, settings: null);
            AppendGuidance(
                report,
                FinishScheduleDiagnosticGuidanceBuilder.Build(preview.Calculation));
        }

        AppendWritePlan(report, preview);
        report.AppendLine();
        report.AppendLine("РЕЗУЛЬТАТ ЗАПИСИ");
        report.AppendLine($"Room-значений записано: {result.AppliedRoomValues}.");
        report.AppendLine(
            $"Ownership: записано {result.AppliedOwnershipValues}; пропущено {result.SkippedOwnershipValues}.");
        if (result.Schedule is not null)
        {
            report.AppendLine(
                $"Спецификация: {result.Schedule.ScheduleName}; id {result.Schedule.ScheduleId}; "
                    + $"действие {result.Schedule.Action}.");
        }

        report.AppendLine($"Сообщение: {result.Message}");
        AppendPerformance(report, result.Performance, "ПРИМЕНЕНИЕ");
        AppendWarnings(
            report,
            ExcludeDiagnosticMessages(
                result.Warnings.Concat(preview.Issues.Select(issue => issue.Message)),
                preview.Calculation?.GeometryWarnings ?? []));
        AppendGeometryDiagnostics(report, preview.Calculation?.GeometryWarnings ?? []);
        return report.ToString().TrimEnd();
    }

    private static StringBuilder Header(string status)
    {
        StringBuilder report = new();
        report.AppendLine("ВЕДОМОСТЬ ОТДЕЛКИ — ОТЧЁТ TRUEBIM");
        report.AppendLine($"Статус: {status}");
        return report;
    }

    private static void AppendCalculation(
        StringBuilder report,
        FinishSchedulePreviewResult preview,
        FinishScheduleSettings? settings)
    {
        report.AppendLine();
        report.AppendLine("РАСЧЁТ");
        report.AppendLine(
            $"Помещения: найдено {preview.CollectedRooms}; обработано {preview.RoomScope.SelectedRooms.Count}; "
                + $"невалидных {preview.RoomScope.InvalidRooms.Count}; вне scope {preview.RoomScope.OutsideScopeCount}.");
        AppendCategory(report, "Стены", settings?.Walls.IsEnabled, preview.Walls, preview.Quantities?.Walls);
        AppendCategory(report, "Полы", settings?.Floors.IsEnabled, preview.Floors, preview.Quantities?.Floors);
        AppendCategory(report, "Потолки", settings?.Ceilings.IsEnabled, preview.Ceilings, preview.Quantities?.Ceilings);
        report.AppendLine(
            $"Spatial index: элементов {preview.Index.IndexedElements}; без bounds {preview.Index.ElementsWithoutBounds}; "
                + $"проверено потенциальных пар {preview.Index.PotentialRoomElementPairs}.");
        if (preview.Aggregation is not null)
        {
            report.AppendLine(
                $"Группировка: групп {preview.Aggregation.GroupCount}; "
                    + $"помещений с output {preview.Aggregation.RoomCount}.");
        }

        AppendPerformance(report, preview.Performance, "ПРОИЗВОДИТЕЛЬНОСТЬ РАСЧЁТА");
    }

    private static void AppendCategory(
        StringBuilder report,
        string name,
        bool? enabled,
        FinishPreviewCategoryCounts counts,
        FinishQuantityCategorySummary? quantities)
    {
        string state = enabled.HasValue
            ? enabled.Value ? "включена" : "отключена"
            : "состояние из плана";
        string geometry = quantities is null
            ? string.Empty
            : $"; связей {quantities.OccurrenceCount}; площадь "
                + $"{quantities.AreaSquareMeters.ToString("N2", RussianCulture)} м²";
        report.AppendLine(
            $"{name} ({state}): собрано {counts.SourceCollected}; классифицировано {counts.Classified}; "
                + $"в scope {counts.InScope}{geometry}.");
    }

    private static void AppendWritePlan(StringBuilder report, FinishScheduleWritePreview preview)
    {
        report.AppendLine();
        report.AppendLine("ПЛАН ИЗМЕНЕНИЙ");
        report.AppendLine($"Групп: {preview.GroupCount}; помещений с output: {preview.RoomCount}.");
        report.AppendLine(
            $"Room-параметры: изменений {preview.RoomPlan.Changes.Count}; "
                + $"без изменений {preview.RoomPlan.UnchangedCount}; заблокировано {preview.RoomPlan.BlockedCount}.");
        report.AppendLine(
            $"Ownership: targets {preview.OwnershipPlan.TargetElementCount}; "
                + $"изменений {preview.OwnershipPlan.Changes.Count}; "
                + $"без изменений {preview.OwnershipPlan.UnchangedCount}; "
                + $"заблокировано {preview.OwnershipPlan.BlockedCount}.");
        report.AppendLine(
            $"Спецификация: {preview.Schedule.Plan?.ScheduleName ?? "не определена"}; "
                + $"действие {preview.Schedule.Action}.");
    }

    private static void AppendPerformance(
        StringBuilder report,
        FinishSchedulePerformanceSummary? performance,
        string title)
    {
        if (performance is null)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine(title);
        foreach (FinishScheduleStageTiming timing in performance.Stages)
        {
            report.AppendLine($"{FormatStage(timing.Stage)}: {timing.ElapsedMilliseconds} мс.");
        }

        FinishScheduleCacheSummary cache = performance.Cache;
        if (cache.TypeEntries > 0
            || cache.Geometry.RoomRequests > 0
            || cache.Geometry.ElementRequests > 0)
        {
            report.AppendLine($"Type cache: entries {cache.TypeEntries}.");
            report.AppendLine(
                $"Room geometry cache: entries {cache.Geometry.RoomEntries}; "
                    + $"requests {cache.Geometry.RoomRequests}; hits {cache.Geometry.RoomHits}.");
            report.AppendLine(
                $"Element geometry cache: entries {cache.Geometry.ElementEntries}; "
                    + $"requests {cache.Geometry.ElementRequests}; hits {cache.Geometry.ElementHits}.");
        }
    }

    private static void AppendWarnings(StringBuilder report, IEnumerable<string> warnings)
    {
        string[] unique = (warnings ?? throw new ArgumentNullException(nameof(warnings)))
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        report.AppendLine();
        report.AppendLine($"ПРЕДУПРЕЖДЕНИЯ ({unique.Length})");
        if (unique.Length == 0)
        {
            report.AppendLine("Нет.");
            return;
        }

        for (int index = 0; index < unique.Length; index++)
        {
            report.AppendLine($"{index + 1}. {unique[index]}");
        }
    }

    private static IEnumerable<string> ExcludeDiagnosticMessages(
        IEnumerable<string> warnings,
        IEnumerable<FinishGeometryWarning> geometryWarnings)
    {
        HashSet<string> diagnosticMessages = new(
            geometryWarnings
                .Where(warning => !FinishGeometryWarningClassifier.AffectsScheduleValue(warning))
                .Select(warning => warning.Message),
            StringComparer.Ordinal);
        return warnings.Where(warning => !diagnosticMessages.Contains(warning));
    }

    private static void AppendGeometryDiagnostics(
        StringBuilder report,
        IEnumerable<FinishGeometryWarning> geometryWarnings)
    {
        FinishGeometryWarning[] diagnostics = geometryWarnings
            .Where(warning => !FinishGeometryWarningClassifier.AffectsScheduleValue(warning))
            .GroupBy(warning => new
            {
                warning.Code,
                warning.RoomId,
                warning.ElementId,
                warning.Category
            })
            .Select(group => group.First())
            .ToArray();
        if (diagnostics.Length == 0)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine($"ДИАГНОСТИКА ГЕОМЕТРИИ ({diagnostics.Length})");
        report.AppendLine(
            "Расчёт завершён без признаков неполных значений. События ниже оставлены для "
                + "технической диагностики; полный текст Revit сохранён в truebim.log.");
        for (int index = 0; index < diagnostics.Length; index++)
        {
            FinishGeometryWarning warning = diagnostics[index];
            report.AppendLine(
                $"{index + 1}. {FormatCategory(warning.Category)}: "
                    + $"помещение {FormatElementId(warning.RoomId)}; "
                    + $"элемент {FormatElementId(warning.ElementId)}; "
                    + $"{FormatDiagnosticCode(warning.Code)}.");
        }
    }

    private static void AppendGuidance(StringBuilder report, IReadOnlyList<string> guidance)
    {
        if (guidance.Count == 0)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine("КАК ИСПРАВИТЬ");
        foreach (string item in guidance)
        {
            report.AppendLine($"• {item}");
        }
    }

    private static string FormatStatus(
        FinishScheduleWriteStatus status,
        bool incompleteCalculation = false)
    {
        if (incompleteCalculation
            && status is FinishScheduleWriteStatus.Applied or FinishScheduleWriteStatus.NoChanges)
        {
            return "ВЫПОЛНЕНО ЧАСТИЧНО";
        }

        return status switch
        {
            FinishScheduleWriteStatus.Applied => "ВЫПОЛНЕНО",
            FinishScheduleWriteStatus.NoChanges => "БЕЗ ИЗМЕНЕНИЙ",
            FinishScheduleWriteStatus.Blocked => "ЗАБЛОКИРОВАНО",
            FinishScheduleWriteStatus.Failed => "ОШИБКА С ОТКАТОМ",
            _ => status.ToString().ToUpperInvariant()
        };
    }

    private static string FormatStage(string stage)
    {
        return stage switch
        {
            FinishScheduleStageNames.CollectCandidates => "Сбор кандидатов",
            FinishScheduleStageNames.ScopeAndIndex => "Scope, классификация и spatial index",
            FinishScheduleStageNames.PhysicalQuantities => "Физическая геометрия и площади",
            FinishScheduleStageNames.Aggregation => "Нормализация и агрегация",
            FinishScheduleStageNames.TotalCalculation => "Итого расчёт",
            FinishScheduleStageNames.OwnershipWrite => "Запись ownership",
            FinishScheduleStageNames.RoomWrite => "Запись Room-параметров",
            FinishScheduleStageNames.ScheduleWrite => "Создание или обновление спецификации",
            FinishScheduleStageNames.TotalApply => "Итого применение",
            _ => stage
        };
    }

    private static string FormatCategory(FinishPreviewCategory? category)
    {
        return category switch
        {
            FinishPreviewCategory.Walls => "Стены",
            FinishPreviewCategory.Floors => "Полы",
            FinishPreviewCategory.Ceilings => "Потолки",
            _ => "Категория не определена"
        };
    }

    private static string FormatElementId(long? elementId)
    {
        return elementId?.ToString(CultureInfo.InvariantCulture) ?? "не определено";
    }

    private static string FormatDiagnosticCode(FinishGeometryWarningCode code)
    {
        return code switch
        {
            FinishGeometryWarningCode.BooleanIntersectionFailed =>
                "проверочное пересечение геометрии не выполнено",
            FinishGeometryWarningCode.ElementNotFound => "элемент не найден при проверке",
            FinishGeometryWarningCode.ElementGeometryUnavailable =>
                "геометрия элемента недоступна для проверки",
            _ => $"диагностический код {code}"
        };
    }
}
