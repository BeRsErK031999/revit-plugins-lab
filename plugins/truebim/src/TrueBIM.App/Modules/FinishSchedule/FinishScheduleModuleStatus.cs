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
                "Модель настроек и безопасные значения по умолчанию готовы",
                "Каталог различает instance/type, категории, тип значения и доступность записи",
                "Identity параметров сохраняет BuiltIn id, shared GUID или project definition ElementId",
                "Правила совместимости объясняют все причины блокировки конфигурации",
                "Полное окно содержит восемь связанных блоков настройки и живую валидацию",
                "Профиль finish-schedule безопасно сохраняется и восстанавливается из JSON",
                "Помещения, стены, перекрытия и уникальные типы собираются один раз для preview",
                "Scope по уровню, секции и всему объекту использует snapshots и bounding-box index",
                "Read-only preview показывает состав области, пропуски и потенциальные пары",
                "Один room solid cache обслуживает boundary и probe-геометрию без повторного расчёта",
                "Физические количества стен, полов и потолков сохраняются как числовые occurrences",
                "Неопределённая геометрия создаёт явные warnings вместо полной площади элемента",
                "Нормализация и агрегация описаний дают детерминированную группировку без площадей в ключе",
                "Повторяющиеся описания суммируются численно внутри помещений и групп без промежуточного округления",
                "Естественная сортировка и финальный formatter синхронно формируют строки description и area",
                "Write preflight проверяет наличие, текстовый StorageType, read-only и worksharing lock до транзакции",
                "Room-параметры записываются идемпотентно и атомарно внутри TransactionGroup",
                "Необязательный ownership пишет реальные помещения и пропускает locked или Unknown элементы с warnings",
                "Предпросмотр не изменяет модель; запись запускается только после явного подтверждения пользователя",
                "Room Schedule создаётся и обновляется только по marker TrueBIM с защитой чужих видов",
                "Scope-фильтры, сортировка, itemization и ширины колонок формируются по контракту V1",
                "Тайминги стадий и cache metrics входят в полный копируемый отчёт",
                "После успешного формирования управляемую спецификацию можно открыть из окна инструмента"
            ],
            [
                "Runtime smoke, installer validation и финальная матрица выпуска"
            ]);
    }
}
