namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleImportCreationResult(
    string ScheduleName,
    long? ScheduleId,
    bool OpenedInSeparateTab,
    int RowCount,
    int ColumnCount,
    int FilterCount,
    bool IsPreview,
    string ConfigurationFingerprint,
    SchedulePreviewTable Preview,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public string ToDialogText()
    {
        if (!Succeeded)
        {
            return string.Join(Environment.NewLine, Errors);
        }

        List<string> lines = IsPreview
            ?
            [
                $"Категория: {ScheduleName}",
                $"Элементов модели в предпросмотре: {RowCount}",
                $"Полей Revit: {ColumnCount}",
                $"Условий: {FilterCount}",
                "Модель не изменена. Создание доступно только для этой проверенной конфигурации."
            ]
            :
            [
                $"Спецификация: {ScheduleName}",
                $"Элементов модели: {RowCount}",
                $"Полей Revit: {ColumnCount}",
                $"Условий: {FilterCount}",
                "Создана параметрическая спецификация Revit из реальных полей элементов модели."
            ];

        if (OpenedInSeparateTab)
        {
            lines.Add("Спецификация открыта в отдельной вкладке Revit.");
        }

        if (Warnings.Count > 0)
        {
            lines.Add("Предупреждения:");
            lines.AddRange(Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
