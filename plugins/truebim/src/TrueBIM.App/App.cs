using Autodesk.Revit.UI;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed class App : IExternalApplication
{
    private const string TabName = "TrueBIM";

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

        RibbonPanel bimPanel = application.CreateRibbonPanel(TabName, TrueBimRibbon.BimPanelName);
        RibbonPanel krPanel = application.CreateRibbonPanel(TabName, TrueBimRibbon.KrPanelName);
        RibbonPanel eomPanel = application.CreateRibbonPanel(TabName, TrueBimRibbon.EomPanelName);
        RibbonPanel ssPanel = application.CreateRibbonPanel(TabName, TrueBimRibbon.SsPanelName);

        Dictionary<string, RibbonPanel> panels = new(StringComparer.Ordinal)
        {
            [TrueBimRibbon.BimPanelName] = bimPanel,
            [TrueBimRibbon.KrPanelName] = krPanel,
            [TrueBimRibbon.EomPanelName] = eomPanel,
            [TrueBimRibbon.SsPanelName] = ssPanel
        };
        foreach (TrueBimRibbonButtonDefinition button in TrueBimRibbon.Buttons)
        {
            AddButton(panels[button.PanelName], button);
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }

    private static void AddButton(
        RibbonPanel panel,
        TrueBimRibbonButtonDefinition button)
    {
        PushButtonData buttonData = new(
            button.Name,
            button.Text,
            typeof(App).Assembly.Location,
            button.CommandClassName);
        buttonData.Image = IconFactory.CreateImage(button.Icon, 16);
        buttonData.LargeImage = IconFactory.CreateImage(button.Icon, 32);
        buttonData.ToolTip = button.Tooltip;
        panel.AddItem(buttonData);
    }
}
