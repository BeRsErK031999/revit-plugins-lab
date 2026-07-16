namespace TrueBIM.App.Modules.FinishSchedule;

public sealed record FinishScheduleModuleStatus(
    string DocumentName,
    bool HasActiveDocument,
    IReadOnlyList<string> ReadyCapabilities,
    IReadOnlyList<string> PendingCapabilities)
{
    public static FinishScheduleModuleStatus Create(string? documentTitle)
    {
        string documentName = string.IsNullOrWhiteSpace(documentTitle)
            ? "Документ Revit не открыт"
            : documentTitle!.Trim();

        return new FinishScheduleModuleStatus(
            documentName,
            !string.IsNullOrWhiteSpace(documentTitle),
            [
                "Раздел АР и кнопка «Ведомость отделки» зарегистрированы в TrueBIM",
                "Команда и окно подключены ко всем целевым сборкам из единой кодовой базы",
                "RoadMap фиксирует моделировочный контракт, этапы и критерии приёмки",
                "Текущий запуск работает без транзакций и не изменяет модель"
            ],
            [
                "Каталог совместимых параметров и модель настроек",
                "Единое окно настройки с валидацией и сохранением профиля",
                "Сбор помещений и физических элементов отделки",
                "Геометрический расчёт, агрегация и безопасное обновление спецификации"
            ]);
    }
}
