namespace TrueBIM.App.Modules.Lintels;

public sealed record LintelsModuleStatus(
    string DocumentName,
    bool CanModifyModel,
    IReadOnlyList<string> ReadyCapabilities,
    IReadOnlyList<string> PendingCapabilities)
{
    public static LintelsModuleStatus Create(string? documentTitle)
    {
        string documentName = string.IsNullOrWhiteSpace(documentTitle)
            ? "Документ Revit не открыт"
            : documentTitle!.Trim();

        return new LintelsModuleStatus(
            documentName,
            false,
            [
                "Кнопка модуля добавлена во вкладку КР",
                "Изолированный namespace и RoadMap подключены",
                "Read-only диагностика выделения и активного вида подключена",
                "Окно выбора и preview будущих имён подключены без изменений модели",
                "Preflight состава, категории именования и дубликатов сборки выполняется без транзакции"
            ],
            [
                "Проверка диагностики и preflight на рабочем RVT-файле",
                "Создание одной подтверждённой сборки и бокового вида",
                "Размеры, отметка, рамка, марки и изображения"
            ]);
    }

    public string ToDialogText()
    {
        string ready = string.Join(Environment.NewLine, ReadyCapabilities.Select(item => $"• {item}"));
        string pending = string.Join(Environment.NewLine, PendingCapabilities.Select(item => $"• {item}"));

        return $"Документ: {DocumentName}{Environment.NewLine}{Environment.NewLine}Готово:{Environment.NewLine}{ready}{Environment.NewLine}{Environment.NewLine}Следующие этапы:{Environment.NewLine}{pending}";
    }
}
