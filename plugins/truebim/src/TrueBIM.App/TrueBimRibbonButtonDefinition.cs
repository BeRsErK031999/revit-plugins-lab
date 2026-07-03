using TrueBIM.App.Commands;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed record TrueBimRibbonButtonDefinition(
    string PanelName,
    string Name,
    string Text,
    string CommandClassName,
    TrueBimIcon Icon,
    string Tooltip);

public static class TrueBimRibbon
{
    public const string BimPanelName = "БИМ";
    public const string KrPanelName = "КР";
    public const string EomPanelName = "ЭОМ";
    public const string SsPanelName = "СС";

    private const string CommandNamespace = "TrueBIM.App.Commands";

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
