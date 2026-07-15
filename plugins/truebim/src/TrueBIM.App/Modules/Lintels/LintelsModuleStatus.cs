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
        bool canModifyModel = !string.IsNullOrWhiteSpace(documentTitle);

        return new LintelsModuleStatus(
            documentName,
            canModifyModel,
            [
                "Кнопка модуля добавлена во вкладку КР",
                "Изолированный namespace и RoadMap подключены",
                "Read-only диагностика выделения и активного вида подключена",
                "Окно выбора и preview будущих имён подключены без изменений модели",
                "Preflight состава, категории именования и дубликатов сборки выполняется без транзакции",
                "Одна подтверждённая сборка создаётся атомарно с защитой от повторного запуска"
            ],
            [
                "Проверка диагностики, preflight и создания сборки на рабочем RVT-файле",
                "Создание бокового вида сборки 1:10",
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
