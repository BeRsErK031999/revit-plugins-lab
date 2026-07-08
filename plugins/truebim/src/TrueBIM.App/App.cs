using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed class App : IExternalApplication
{
    private const string TabName = "TrueBIM";

    public Result OnStartup(UIControlledApplication application)
    {
        application.ViewActivated += OnViewActivated;

        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch
        {
            // Revit throws if the tab already exists.
        }

        Dictionary<string, RibbonPanel> panels = new(StringComparer.Ordinal);
        foreach (string panelName in TrueBimRibbon.PanelNames)
        {
            panels[panelName] = application.CreateRibbonPanel(TabName, panelName);
        }

        string revitVersion = application.ControlledApplication.VersionNumber;
        foreach (TrueBimRibbonButtonDefinition button in TrueBimRibbon.Buttons)
        {
            if (!TrueBimRibbon.IsButtonAvailableForRevitVersion(button, revitVersion))
            {
                continue;
            }

            AddButton(panels[button.PanelName], button);
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.ViewActivated -= OnViewActivated;
        ScheduleActiveViewTracker.Clear();

        return Result.Succeeded;
    }

    private static void OnViewActivated(object? sender, ViewActivatedEventArgs args)
    {
        try
        {
            ScheduleActiveViewTracker.CaptureActivatedView(args.CurrentActiveView);
        }
        catch
        {
        }
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
        buttonData.LongDescription = button.LongDescription;
        panel.AddItem(buttonData);
    }
}
