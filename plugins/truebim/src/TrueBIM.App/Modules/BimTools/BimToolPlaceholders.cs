using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools;

public static class BimToolPlaceholders
{
    public static BimToolPlaceholderDefinition ColorByParameter { get; } = new(
        "Цвета по параметрам",
        TrueBimIcon.ColorByParameter,
        "Каркас инструмента для раскраски элементов активного вида по значениям выбранного параметра.",
        [
            "Выбор категорий на активном виде.",
            "Выбор параметра и просмотр уникальных значений.",
            "Настройка цветов и применение фильтров к активному виду.",
            "Очистка только фильтров с префиксом BIM_F_."
        ],
        [
            "Сервис чтения параметров с учетом StorageType.",
            "Генератор безопасных имен фильтров.",
            "Создание и обновление ParameterFilterElement.",
            "Отчет по обработанным, пропущенным и ошибочным значениям."
        ]);

    public static BimToolPlaceholderDefinition CopyParameters { get; } = new(
        "Копирование параметров",
        TrueBimIcon.CopyParameters,
        "Каркас инструмента для копирования выбранных значений параметров с исходного элемента на элементы-получатели.",
        [
            "Выбор исходного элемента или использование единственного предвыбранного.",
            "Выбор параметров, доступных для копирования.",
            "Выбор элементов-получателей на виде.",
            "Копирование внутри Transaction с отчетом по пропускам."
        ],
        [
            "Сервис совместимости параметров по GUID, BuiltInParameter и имени.",
            "Копирование значений String, Integer, Double и ElementId без строковой конвертации.",
            "Предупреждения для системных и потенциально опасных параметров.",
            "Отчет по отсутствующим, read-only и несовместимым параметрам."
        ]);

    public static BimToolPlaceholderDefinition ParaManager { get; } = new(
        "ParaManager",
        TrueBimIcon.Parameters,
        "Каркас MVP для импорта shared parameters в проект из CSV или Excel.",
        [
            "Выбор CSV или Excel-файла с параметрами.",
            "Выбор shared parameter .txt.",
            "Предпросмотр и валидация строк до применения.",
            "Создание определений и привязок параметров внутри Transaction."
        ],
        [
            "Чтение шаблона ParameterName, SharedGroup, BindingType, Categories, GroupUnder, DataType.",
            "Создание групп и ExternalDefinition в shared parameter file.",
            "Привязка instance/type параметров к найденным категориям.",
            "Отчет по созданным, привязанным, пропущенным и конфликтующим параметрам."
        ]);

    public static BimToolPlaceholderDefinition CreateWorksets { get; } = new(
        "Рабочие наборы",
        TrueBimIcon.Worksets,
        "Каркас инструмента для создания рабочих наборов из проверенного CSV или Excel-шаблона.",
        [
            "Выбор файла с именами рабочих наборов.",
            "Предпросмотр строк и проверка дубликатов.",
            "Явное подтверждение перед включением worksharing.",
            "Создание рабочих наборов внутри Transaction с итоговым отчетом."
        ],
        [
            "CSV-ридер как первый стабильный формат.",
            "Проверка имени, пустых строк, дублей и существующих worksets.",
            "Безопасная обертка над CanEnableWorksharing и EnableWorksharing.",
            "Отчет по созданным, существующим, пропущенным и ошибочным строкам."
        ]);
}
