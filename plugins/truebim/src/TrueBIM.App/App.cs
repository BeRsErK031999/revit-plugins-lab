using Autodesk.Revit.UI;
using TrueBIM.App.Commands;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed class App : IExternalApplication
{
    private const string TabName = "TrueBIM";
    private const string PanelName = "TrueBIM";

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

        RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);

        AddButton(
            panel,
            "TrueBIM_SheetNumbering",
            "Нумератор\nлистов",
            typeof(OpenSheetNumberingCommand).FullName,
            TrueBimIcon.SheetNumbering,
            "Открывает нумератор листов TrueBIM.");

        AddButton(
            panel,
            "TrueBIM_CollapseScheduleColumns",
            "Свернуть\nВРС",
            typeof(CollapseScheduleColumnsCommand).FullName,
            TrueBimIcon.ScheduleCollapse,
            "Создаёт копию спецификации и скрывает нулевые столбцы.");

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
