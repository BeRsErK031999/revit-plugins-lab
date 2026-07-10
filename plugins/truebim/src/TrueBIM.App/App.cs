using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.BimTools.FamilyManager.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.Modules.ViewVisibility.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.Services.Runtime;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed class App : IExternalApplication
{
    private const string TabName = "TrueBIM";

    public Result OnStartup(UIControlledApplication application)
    {
        NetFrameworkAssemblyResolver.Register();
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

        foreach (TrueBimRibbonButtonDefinition button in TrueBimRibbon.Buttons)
        {
            AddButton(panels[button.PanelName], button);
        }

        RegisterFamilyManagerPane(application);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.ViewActivated -= OnViewActivated;
        ScheduleActiveViewTracker.Clear();
        NetFrameworkAssemblyResolver.Unregister();

        return Result.Succeeded;
    }

    private static void OnViewActivated(object? sender, ViewActivatedEventArgs args)
    {
        try
        {
            ScheduleActiveViewTracker.CaptureActivatedView(args.CurrentActiveView);
            ViewVisibilityRibbonState.Update(args.CurrentActiveView?.Document, args.CurrentActiveView);
        }
        catch
        {
        }
    }

    private static void AddButton(
        RibbonPanel panel,
        TrueBimRibbonButtonDefinition button)
    {
        if (button.IsPulldown)
        {
            AddPulldownButton(panel, button);
            return;
        }

        panel.AddItem(CreatePushButtonData(button));
    }

    private static void AddPulldownButton(
        RibbonPanel panel,
        TrueBimRibbonButtonDefinition button)
    {
        PulldownButtonData pulldownData = new(button.Name, button.Text);
        PulldownButton pulldown = (PulldownButton)panel.AddItem(pulldownData);
        pulldown.Image = IconFactory.CreateImage(button.Icon, 16);
        pulldown.LargeImage = IconFactory.CreateImage(button.Icon, 32);
        pulldown.ToolTip = button.Tooltip;
        pulldown.LongDescription = button.LongDescription;
        if (string.Equals(button.Name, "TrueBIM_ViewVisibility", StringComparison.Ordinal))
        {
            ViewVisibilityRibbonState.RegisterPulldown(pulldown);
        }

        foreach (TrueBimRibbonPulldownItemDefinition item in button.Items)
        {
            if (item.BeginsGroup)
            {
                pulldown.AddSeparator();
            }

            PushButton pushButton = pulldown.AddPushButton(CreatePushButtonData(item));
            if (string.Equals(button.Name, "TrueBIM_ViewVisibility", StringComparison.Ordinal))
            {
                ViewVisibilityRibbonState.RegisterItem(item, pushButton);
            }
        }
    }

    private static PushButtonData CreatePushButtonData(TrueBimRibbonButtonDefinition button)
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
        if (!string.IsNullOrWhiteSpace(button.AvailabilityClassName))
        {
            buttonData.AvailabilityClassName = button.AvailabilityClassName;
        }

        return buttonData;
    }

    private static PushButtonData CreatePushButtonData(TrueBimRibbonPulldownItemDefinition item)
    {
        PushButtonData buttonData = new(
            item.Name,
            item.Text,
            typeof(App).Assembly.Location,
            item.CommandClassName);
        buttonData.Image = IconFactory.CreateImage(item.Icon, 16);
        buttonData.LargeImage = IconFactory.CreateImage(item.Icon, 32);
        buttonData.ToolTip = item.Tooltip;

        return buttonData;
    }

    private static void RegisterFamilyManagerPane(UIControlledApplication application)
    {
        try
        {
            FamilyManagerDockablePaneProvider.Register(application);
        }
        catch (Exception exception)
        {
            FileTrueBimLogger logger = new(new TrueBimLogPaths());
            logger.Error("Failed to register Family Manager dockable pane.", exception);
        }
    }
}
