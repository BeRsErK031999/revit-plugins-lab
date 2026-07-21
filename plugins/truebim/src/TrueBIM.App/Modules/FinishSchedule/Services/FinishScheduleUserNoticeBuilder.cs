using System.Globalization;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishScheduleUserNoticeBuilder
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public FinishScheduleUserNotice BuildPreview(
        FinishSchedulePreviewResult preview,
        FinishScheduleSettings settings)
    {
        return BuildCalculation(
            preview ?? throw new ArgumentNullException(nameof(preview)),
            settings ?? throw new ArgumentNullException(nameof(settings)),
            additionalWarnings: [],
            successTitle: "Расчёт готов",
            successMessage: "Помещения и отделка сопоставлены. Можно формировать ведомость.");
    }

    public FinishScheduleUserNotice BuildWritePreview(
        FinishScheduleWritePreview preview,
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

        if (preview.Calculation is null)
        {
            return CreateActionNotice(
                preview.CanApply ? "Проверка завершена" : "Перед формированием нужна проверка",
                preview.CanApply
                    ? "План записи подготовлен. Можно продолжать."
                    : "Запись не началась. Исправьте пункты ниже и повторите формирование.",
                [
                    $"Помещений в плане: {preview.RoomCount}.",
                    $"Изменений: {preview.TotalChangeCount}."
                ],
                preview.Issues.Select(issue => issue.Message),
                preview.CanApply ? FinishScheduleUserNoticeSeverity.Info : FinishScheduleUserNoticeSeverity.Danger);
        }

        FinishScheduleUserNotice notice = BuildCalculation(
            preview.Calculation,
            settings,
            preview.Issues.Select(issue => issue.Message),
            successTitle: "Проверка перед формированием пройдена",
            successMessage: "Расчёт готов, план записи подготовлен. Можно продолжать.");
        if (preview.CanApply)
        {
            return notice;
        }

        return new FinishScheduleUserNotice(
            "Перед формированием нужна проверка",
            "Запись не началась. Исправьте пункты ниже и повторите формирование.",
            notice.SummaryItems,
            notice.WarningItems,
            Math.Max(notice.IssueCount, preview.Issues.Count),
            FinishScheduleUserNoticeSeverity.Danger);
    }

    public FinishScheduleUserNotice BuildResult(
        FinishScheduleWritePreview preview,
        FinishScheduleWriteResult result,
        FinishScheduleSettings settings)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (preview.Calculation is null)
        {
            return CreateActionNotice(
                result.Succeeded ? "Ведомость готова" : "Не удалось сформировать ведомость",
                HumanizeWarning(result.Message),
                [$"Записано значений помещений: {result.AppliedRoomValues}."],
                result.Warnings,
                result.Succeeded ? FinishScheduleUserNoticeSeverity.Success : FinishScheduleUserNoticeSeverity.Danger);
        }

        FinishScheduleUserNotice notice = BuildCalculation(
            preview.Calculation,
            settings,
            result.Warnings.Concat(preview.Issues.Select(issue => issue.Message)),
            successTitle: "Ведомость готова",
            successMessage: HumanizeWarning(result.Message));
        if (result.Succeeded)
        {
            return notice;
        }

        return new FinishScheduleUserNotice(
            "Не удалось сформировать ведомость",
            HumanizeWarning(result.Message),
            notice.SummaryItems,
            notice.WarningItems,
            Math.Max(1, notice.IssueCount),
            FinishScheduleUserNoticeSeverity.Danger);
    }

    public FinishScheduleUserNotice CreateActionNotice(
        string title,
        string message,
        IEnumerable<string> summaryItems,
        IEnumerable<string> warnings,
        FinishScheduleUserNoticeSeverity severity)
    {
        string[] warningItems = (warnings ?? throw new ArgumentNullException(nameof(warnings)))
            .Select(HumanizeWarning)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        FinishScheduleUserNoticeSeverity resolvedSeverity = warningItems.Length switch
        {
            0 when severity == FinishScheduleUserNoticeSeverity.Warning => FinishScheduleUserNoticeSeverity.Info,
            > 0 when severity is FinishScheduleUserNoticeSeverity.Info or FinishScheduleUserNoticeSeverity.Success =>
                FinishScheduleUserNoticeSeverity.Warning,
            _ => severity
        };
        return new FinishScheduleUserNotice(
            title,
            message,
            summaryItems,
            warningItems,
            warningItems.Length,
            resolvedSeverity);
    }

    private static FinishScheduleUserNotice BuildCalculation(
        FinishSchedulePreviewResult preview,
        FinishScheduleSettings settings,
        IEnumerable<string> additionalWarnings,
        string successTitle,
        string successMessage)
    {
        List<string> summary =
        [
            FormatRooms(preview)
        ];
        AppendCategory(summary, "Стены", settings.Walls.IsEnabled, preview.Walls, preview.Quantities?.Walls);
        AppendCategory(summary, "Полы", settings.Floors.IsEnabled, preview.Floors, preview.Quantities?.Floors);
        AppendCategory(summary, "Потолки", settings.Ceilings.IsEnabled, preview.Ceilings, preview.Quantities?.Ceilings);
        if (preview.Aggregation is not null)
        {
            summary.Add($"Групп помещений: {preview.Aggregation.GroupCount}.");
        }

        HashSet<string> geometryMessages = preview.GeometryWarnings
            .Select(warning => warning.Message)
            .ToHashSet(StringComparer.Ordinal);
        string[] generalWarnings = preview.Warnings
            .Concat(additionalWarnings ?? throw new ArgumentNullException(nameof(additionalWarnings)))
            .Where(warning => !geometryMessages.Contains(warning))
            .Select(HumanizeWarning)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] geometryWarnings = preview.GeometryWarnings
            .GroupBy(warning => $"{warning.Code}:{warning.Category}", StringComparer.Ordinal)
            .Select(FormatGeometryWarningGroup)
            .ToArray();
        string[] missingLinks = BuildMissingLinkWarnings(preview, settings).ToArray();
        string[] warningItems = geometryWarnings
            .Concat(missingLinks)
            .Concat(generalWarnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        int issueCount = preview.GeometryWarnings.Count + missingLinks.Length + generalWarnings.Length;
        bool incomplete = FinishGeometryWarningClassifier.HasIncompleteScheduleValues(preview);
        if (warningItems.Length == 0)
        {
            return new FinishScheduleUserNotice(
                successTitle,
                successMessage,
                summary,
                [],
                0,
                FinishScheduleUserNoticeSeverity.Success);
        }

        bool hasGeometryConcerns = geometryWarnings.Length > 0 || missingLinks.Length > 0;
        return new FinishScheduleUserNotice(
            incomplete ? "Часть отделки нужно проверить" : "Расчёт готов, есть замечания",
            incomplete
                ? "Некоторые площади могли рассчитаться не полностью. Ниже указано, что проверить в модели."
                : hasGeometryConcerns
                    ? "Расчёт завершён. Несколько сложных мест лучше сверить перед выпуском."
                    : "Расчёт завершён. Перед формированием проверьте замечания ниже.",
            summary,
            warningItems,
            Math.Max(issueCount, warningItems.Length),
            FinishScheduleUserNoticeSeverity.Warning);
    }

    private static string FormatRooms(FinishSchedulePreviewResult preview)
    {
        string result = $"Помещения: рассчитано {preview.RoomScope.SelectedRooms.Count} из {preview.CollectedRooms}.";
        if (preview.RoomScope.InvalidRooms.Count > 0)
        {
            result += $" Без корректной геометрии: {preview.RoomScope.InvalidRooms.Count}.";
        }

        return result;
    }

    private static void AppendCategory(
        List<string> summary,
        string name,
        bool enabled,
        FinishPreviewCategoryCounts counts,
        FinishQuantityCategorySummary? quantities)
    {
        if (!enabled)
        {
            summary.Add($"{name}: отключены для этого расчёта.");
            return;
        }

        if (quantities is null)
        {
            summary.Add($"{name}: найдено элементов — {counts.InScope}; площадь ещё не рассчитана.");
            return;
        }

        summary.Add(
            $"{name}: найдено элементов — {counts.InScope}; участков в помещениях — {quantities.OccurrenceCount}; "
                + $"площадь — {quantities.AreaSquareMeters.ToString("N2", RussianCulture)} м².");
    }

    private static IEnumerable<string> BuildMissingLinkWarnings(
        FinishSchedulePreviewResult preview,
        FinishScheduleSettings settings)
    {
        if (settings.Floors.IsEnabled
            && preview.Floors.InScope > 0
            && preview.Quantities?.Floors.OccurrenceCount == 0)
        {
            yield return "Полы найдены, но ни один не сопоставлен с помещением. Проверьте нижнюю границу помещений и фактический контакт с полом.";
        }

        if (settings.Ceilings.IsEnabled
            && preview.Ceilings.InScope > 0
            && preview.Quantities?.Ceilings.OccurrenceCount == 0)
        {
            yield return "Потолки найдены, но ни один не сопоставлен с помещением. Проверьте верхнюю границу помещений, «Границу помещения» и фактический контакт с потолком.";
        }
    }

    private static string FormatGeometryWarningGroup(
        IGrouping<string, FinishGeometryWarning> group)
    {
        FinishGeometryWarning first = group.First();
        int count = group.Count();
        string category = first.Category switch
        {
            FinishPreviewCategory.Walls => "стен",
            FinishPreviewCategory.Floors => "полов",
            FinishPreviewCategory.Ceilings => "потолков",
            _ => "отделки"
        };
        string message = first.Code switch
        {
            FinishGeometryWarningCode.BooleanIntersectionFailed =>
                $"Сложных пересечений {category}: {count}. Revit не смог выполнить точную геометрическую проверку; расчёт продолжен другими доступными способами.",
            FinishGeometryWarningCode.RoomNotFound =>
                $"Помещений, которые стали недоступны во время расчёта: {count}. Обновите предпросмотр.",
            FinishGeometryWarningCode.RoomGeometryUnavailable =>
                $"Помещений без доступной геометрии: {count}. Проверьте их размещение и границы.",
            FinishGeometryWarningCode.ElementNotFound =>
                $"Элементов {category}, которые стали недоступны во время расчёта: {count}. Обновите предпросмотр.",
            FinishGeometryWarningCode.ElementGeometryUnavailable =>
                $"Элементов {category} без доступной геометрии: {count}. Проверьте их состояние в модели.",
            FinishGeometryWarningCode.WallFallbackUnresolved =>
                $"Участков стен, площадь которых не удалось надёжно определить: {count}.",
            FinishGeometryWarningCode.SlabGeometryUnsupported =>
                $"Элементов {category} со слишком сложной формой для точного расчёта: {count}.",
            FinishGeometryWarningCode.ProbeCreationFailed =>
                $"Помещений, для которых не удалось построить область проверки {category}: {count}.",
            FinishGeometryWarningCode.ProjectedAreaUnavailable =>
                $"Пересечений {category}, для которых не удалось определить площадь: {count}.",
            _ => $"Геометрических замечаний для {category}: {count}."
        };

        return message + FormatAffectedIds(group);
    }

    private static string FormatAffectedIds(IEnumerable<FinishGeometryWarning> warnings)
    {
        FinishGeometryWarning[] items = warnings.ToArray();
        string rooms = FormatIds(items.Where(item => item.RoomId.HasValue).Select(item => item.RoomId!.Value));
        string elements = FormatIds(items.Where(item => item.ElementId.HasValue).Select(item => item.ElementId!.Value));
        return (string.IsNullOrEmpty(rooms) ? string.Empty : $" Помещения: {rooms}.")
            + (string.IsNullOrEmpty(elements) ? string.Empty : $" Элементы: {elements}.");
    }

    private static string FormatIds(IEnumerable<long> ids)
    {
        long[] distinct = ids.Distinct().OrderBy(id => id).ToArray();
        const int visibleLimit = 20;
        string result = string.Join(", ", distinct.Take(visibleLimit));
        return distinct.Length > visibleLimit
            ? $"{result} и ещё {distinct.Length - visibleLimit}"
            : result;
    }

    private static string HumanizeWarning(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return string.Empty;
        }

        string value = warning.Trim();
        if (ContainsIgnoreCase(value, "Failed to")
            || ContainsIgnoreCase(value, "Exception")
            || ContainsIgnoreCase(value, "Boolean operation")
            || ContainsIgnoreCase(value, "Spatial index")
            || ContainsIgnoreCase(value, "cache hit"))
        {
            return "Revit сообщил о технической проблеме во время расчёта. Исходная запись доступна в полном отчёте.";
        }

        return value
            .Replace("preflight", "предварительной проверки")
            .Replace("scope", "выбранной области")
            .Replace("output", "результат");
    }

    private static bool ContainsIgnoreCase(string value, string phrase)
    {
        return value.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
