using TrueBIM.App.Commands;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed record TrueBimRibbonButtonDefinition(
    string PanelName,
    string Name,
    string Text,
    string CommandClassName,
    TrueBimIcon Icon,
    string Tooltip,
    string LongDescription = "");

public static class TrueBimRibbon
{
    public const string BimPanelName = "БИМ";
    public const string ModelReviewPanelName = "Проверка модели";
    public const string GeometryPanelName = "Геометрия";
    public const string ParametersPanelName = "Параметры";
    public const string AdministrationPanelName = "Администрирование";
    public const string KrPanelName = "КР";
    public const string EomPanelName = "ЭОМ";
    public const string SsPanelName = "СС";

    private const string CommandNamespace = "TrueBIM.App.Commands";

    public static IReadOnlyList<string> PanelNames { get; } =
    [
        BimPanelName,
        ModelReviewPanelName,
        GeometryPanelName,
        ParametersPanelName,
        AdministrationPanelName,
        KrPanelName,
        EomPanelName,
        SsPanelName
    ];

    public static IReadOnlyList<TrueBimRibbonButtonDefinition> Buttons { get; } =
    [
        new(
            BimPanelName,
            "TrueBIM_SheetNumbering",
            "Нумератор\nлистов",
            $"{CommandNamespace}.{nameof(OpenSheetNumberingCommand)}",
            TrueBimIcon.SheetNumbering,
            "Открывает нумератор листов TrueBIM."),
        new(
            BimPanelName,
            "TrueBIM_Print",
            "Печать",
            $"{CommandNamespace}.{nameof(OpenPrintCommand)}",
            TrueBimIcon.Print,
            "Открывает модуль печати и экспорта листов TrueBIM."),
        new(
            BimPanelName,
            "TrueBIM_ViewVisibility",
            "Видимость",
            $"{CommandNamespace}.{nameof(OpenViewVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Включает и выключает видимость категорий на активном виде."),
        new(
            BimPanelName,
            "TrueBIM_IsoFieldRebar",
            "Армирование\nпо изополям",
            $"{CommandNamespace}.{nameof(IsoFieldRebarCommand)}",
            TrueBimIcon.IsoFieldRebar,
            "Открывает экспериментальный модуль армирования по изополям.",
            "Безопасная заглушка: показывает статус подключения без распознавания, OpenCV/Python и создания арматуры."),
        new(
            ModelReviewPanelName,
            "TrueBIM_ColorByParameter",
            "Цвета\nпо параметрам",
            $"{CommandNamespace}.{nameof(ColorByParameterCommand)}",
            TrueBimIcon.ColorByParameter,
            "Открывает подготовку раскраски активного вида по значениям выбранного параметра.",
            "Инструмент BIM-проверки для будущего выбора категорий, параметра, уникальных значений и применения фильтров с префиксом BIM_F_."),
        new(
            GeometryPanelName,
            "TrueBIM_JoinCut",
            "Соединить /\nВырезать",
            $"{CommandNamespace}.{nameof(JoinCutCommand)}",
            TrueBimIcon.JoinCut,
            "Открывает настройку правил соединения и вырезания геометрии.",
            "MVP-инструмент для конфигураций, правил соединения/вырезания, предпросмотра, выполнения и отчета по обработке геометрии."),
        new(
            ParametersPanelName,
            "TrueBIM_CopyParameters",
            "Копирование\nпараметров",
            $"{CommandNamespace}.{nameof(CopyParametersCommand)}",
            TrueBimIcon.CopyParameters,
            "Открывает подготовку копирования выбранных параметров между элементами.",
            "Инструмент для будущего выбора исходного элемента, копируемых параметров, элементов-получателей и отчета по пропущенным значениям."),
        new(
            ParametersPanelName,
            "TrueBIM_ParaManager",
            "ParaManager",
            $"{CommandNamespace}.{nameof(ParaManagerCommand)}",
            TrueBimIcon.Parameters,
            "Открывает подготовку управления shared parameters и параметрами проекта.",
            "MVP-инструмент для будущего импорта shared parameters из CSV или Excel, предпросмотра привязок и отчета по созданным параметрам."),
        new(
            AdministrationPanelName,
            "TrueBIM_CreateWorksets",
            "Рабочие\nнаборы",
            $"{CommandNamespace}.{nameof(CreateWorksetsCommand)}",
            TrueBimIcon.Worksets,
            "Открывает подготовку создания рабочих наборов из CSV или Excel.",
            "Инструмент администрирования для будущей проверки шаблона, включения worksharing только с подтверждением и создания worksets с отчетом."),
        new(
            KrPanelName,
            "TrueBIM_CollapseScheduleColumns",
            "Свернуть\nВРС",
            $"{CommandNamespace}.{nameof(CollapseScheduleColumnsCommand)}",
            TrueBimIcon.ScheduleCollapse,
            "Скрывает нулевые столбцы в выбранной спецификации."),
        new(
            EomPanelName,
            "TrueBIM_VoltageDropCalculation",
            "Расчет\nпотери\nнапряжения",
            $"{CommandNamespace}.{nameof(OpenVoltageDropCalculationCommand)}",
            TrueBimIcon.VoltageDrop,
            "Открывает расчет потери напряжения и нагрузок по данным первого листа Excel.")
    ];
}
