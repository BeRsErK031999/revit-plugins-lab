using Autodesk.Revit.UI;
using TrueBIM.App.Commands;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed class App : IExternalApplication
{
    private const string TabName = "TrueBIM";
    private const string BimPanelName = "БИМ";
    private const string KrPanelName = "КР";
    private const string EomPanelName = "ЭОМ";
    private const string SsPanelName = "СС";

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch
        {
            // Revit throws if the tab already exists.
        }

        RibbonPanel bimPanel = application.CreateRibbonPanel(TabName, BimPanelName);
        RibbonPanel krPanel = application.CreateRibbonPanel(TabName, KrPanelName);
        application.CreateRibbonPanel(TabName, EomPanelName);
        application.CreateRibbonPanel(TabName, SsPanelName);

        AddButton(
            bimPanel,
            "TrueBIM_SheetNumbering",
            "Нумератор\nлистов",
            typeof(OpenSheetNumberingCommand).FullName,
            TrueBimIcon.SheetNumbering,
            "Открывает нумератор листов TrueBIM.");

        AddButton(
            bimPanel,
            "TrueBIM_Print",
            "Печать",
            typeof(OpenPrintCommand).FullName,
            TrueBimIcon.Print,
            "Открывает модуль печати и экспорта листов TrueBIM.");

        AddButton(
            krPanel,
            "TrueBIM_CollapseScheduleColumns",
            "Свернуть\nВРС",
            typeof(CollapseScheduleColumnsCommand).FullName,
            TrueBimIcon.ScheduleCollapse,
            "Скрывает нулевые столбцы в выбранной спецификации.");

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }

    private static void AddButton(
        RibbonPanel panel,
        string name,
        string text,
        string? commandClassName,
        TrueBimIcon icon,
        string tooltip)
    {
        PushButtonData buttonData = new(
            name,
            text,
            typeof(App).Assembly.Location,
            commandClassName);
        buttonData.Image = IconFactory.CreateImage(icon, 16);
        buttonData.LargeImage = IconFactory.CreateImage(icon, 32);
        buttonData.ToolTip = tooltip;
        panel.AddItem(buttonData);
    }
}
