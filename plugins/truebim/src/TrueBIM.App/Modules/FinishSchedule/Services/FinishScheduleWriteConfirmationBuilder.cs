using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed record FinishScheduleWriteConfirmation(string Message, bool ReplacesExistingValues);

public sealed class FinishScheduleWriteConfirmationBuilder
{
    public FinishScheduleWriteConfirmation Build(FinishScheduleWritePreview preview)
    {
        FinishParameterChange[] roomValues = ContentChanges(preview.RoomPlan);
        FinishParameterChange[] ownershipValues = ContentChanges(preview.OwnershipPlan);
        FinishParameterChange[] contentChanges = roomValues.Concat(ownershipValues).ToArray();
        int formattingCount = preview.TotalChangeCount - contentChanges.Length;
        int replacedCount = contentChanges.Count(change => !string.IsNullOrWhiteSpace(change.PreviousValue));
        int clearedCount = contentChanges.Count(change => string.IsNullOrWhiteSpace(change.NewValue));
        string name = preview.Schedule.Plan?.ScheduleName ?? "Ведомость отделки помещений";
        List<string> paragraphs =
        [
            $"Будет создана новая версия «{name}» с данными текущего расчёта. "
            + "Прежние версии TrueBIM сохранят свои значения и размещение на листах."
        ];
        if (preview.Schedule.LegacyScheduleIds.Count > 0)
        {
            paragraphs.Add($"Ведомостей из прежней версии плагина: {preview.Schedule.LegacyScheduleIds.Count}. "
                           + "Перед записью их текущие данные будут сохранены в архивных таблицах.");
        }

        paragraphs.Add(roomValues.Length == 0
            ? "Площади, описания и перечни помещений уже совпадают с расчётом."
            : $"В параметрах помещений изменятся значения: {roomValues.Length}. "
              + $"Затронуто помещений: {roomValues.Select(change => change.ElementId).Distinct().Count()}.");
        if (ownershipValues.Length > 0)
        {
            paragraphs.Add($"Обновится принадлежность отделочных элементов помещениям: "
                           + $"{ownershipValues.Select(change => change.ElementId).Distinct().Count()} элементов.");
        }

        if (formattingCount > 0)
        {
            paragraphs.Add($"Только оформление текста: {formattingCount} значений. "
                           + "Изменятся переносы строк и пробелы для выравнивания; сами числа и текст сохранятся.");
        }

        if (preview.TotalChangeCount == 0)
        {
            paragraphs.Add("Параметры не будут перезаписаны. Будет добавлена только новая версия ведомости.");
        }

        if (replacedCount > 0)
        {
            paragraphs.Add($"Будут заменены заполненные значения: {replacedCount}. "
                           + "Ручные правки в этих выходных параметрах заменятся результатом расчёта. "
                           + "Другие обычные спецификации, читающие эти параметры, также покажут новые значения.");
        }

        if (clearedCount > 0)
        {
            paragraphs.Add($"Будут очищены значения: {clearedCount}. Проверьте примеры ниже и полный отчёт перед созданием.");
        }

        string[] samples = contentChanges
            .OrderByDescending(change => string.IsNullOrWhiteSpace(change.NewValue))
            .ThenByDescending(change => !string.IsNullOrWhiteSpace(change.PreviousValue))
            .Take(3)
            .Select(change =>
            {
                string label = preview.ElementLabels.TryGetValue(change.ElementId, out string? value)
                    ? value : $"Элемент {change.ElementId}";
                return $"• {label}, {change.Role}: «{Compact(change.PreviousValue)}» → «{Compact(change.NewValue)}»";
            }).ToArray();
        if (samples.Length > 0)
        {
            paragraphs.Add("Примеры изменения значений:\n" + string.Join("\n", samples));
        }

        paragraphs.Add("При ошибке создание версии и изменения параметров будут отменены вместе.\n\nСоздать новую версию?");
        return new FinishScheduleWriteConfirmation(string.Join("\n\n", paragraphs), replacedCount > 0);
    }

    private static FinishParameterChange[] ContentChanges(FinishWritePlan plan)
    {
        return plan.Changes.Where(change => !FinishParameterValueComparison.IsFormattingOnly(
            change.PreviousValue, change.NewValue)).ToArray();
    }

    private static string Compact(string value)
    {
        string text = FinishParameterValueComparison.ReadableValue(value);
        return text.Length == 0 ? "пусто" : text.Length <= 90 ? text : text.Substring(0, 87) + "…";
    }
}
