using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.BimTools.FamilyManager.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.Modules.ViewVisibility.UI;
using TrueBIM.App.Modules.SelectionBox;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.Services.Runtime;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

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

        foreach (TrueBimRibbonPanelDefinition definition in TrueBimRibbon.Panels)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, definition.Name);
            if (definition.IsStacked)
            {
                AddStackedButtons(panel, definition.Buttons);
            }
            else
            {
                foreach (TrueBimRibbonButtonDefinition button in definition.Buttons)
                {
                    InitializeButton(panel.AddItem(CreateButtonData(button)), button);
                }
            }
        }

        RegisterFamilyManagerPane(application);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        SelectionBoxRequest.CancelPending();
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
            SelectionBoxViews.Record(args.CurrentActiveView);
            ViewVisibilityRibbonState.Update(args.CurrentActiveView?.Document, args.CurrentActiveView);
        }
        catch
        {
        }
    }

    private static void AddStackedButtons(
        RibbonPanel panel,
        IReadOnlyList<TrueBimRibbonButtonDefinition> buttons)
    {
        if (buttons.Count == 4)
        {
            AddStackedButtons(panel, buttons.Take(2).ToArray());
            AddStackedButtons(panel, buttons.Skip(2).ToArray());
            return;
        }

        IList<RibbonItem> items = buttons.Count switch
        {
            2 => panel.AddStackedItems(CreateButtonData(buttons[0]), CreateButtonData(buttons[1])),
            3 => panel.AddStackedItems(CreateButtonData(buttons[0]), CreateButtonData(buttons[1]), CreateButtonData(buttons[2])),
            _ => throw new InvalidOperationException("A ribbon stack must contain two or three buttons.")
        };

        for (int index = 0; index < items.Count; index++)
        {
            InitializeButton(items[index], buttons[index]);
        }
    }

    private static RibbonItemData CreateButtonData(TrueBimRibbonButtonDefinition button)
    {
        return button.IsPulldown
            ? new PulldownButtonData(button.Name, button.Text)
            : CreatePushButtonData(button);
    }

    private static void InitializeButton(RibbonItem ribbonItem, TrueBimRibbonButtonDefinition button)
    {
        if (ribbonItem is not PulldownButton pulldown)
        {
            return;
        }

        pulldown.Image = IconFactory.CreateImage(button.Icon, TrueBimTheme.IconSizeRibbonSmall);
        pulldown.LargeImage = IconFactory.CreateImage(button.Icon, TrueBimTheme.IconSizeRibbon);
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
        buttonData.Image = IconFactory.CreateImage(button.Icon, TrueBimTheme.IconSizeRibbonSmall);
        buttonData.LargeImage = IconFactory.CreateImage(button.Icon, TrueBimTheme.IconSizeRibbon);
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
        buttonData.Image = IconFactory.CreateImage(item.Icon, TrueBimTheme.IconSizeRibbonSmall);
        buttonData.LargeImage = IconFactory.CreateImage(item.Icon, TrueBimTheme.IconSizeRibbon);
        buttonData.ToolTip = item.Tooltip;
        buttonData.LongDescription = item.LongDescription;
        if (!string.IsNullOrWhiteSpace(item.AvailabilityClassName))
        {
            buttonData.AvailabilityClassName = item.AvailabilityClassName;
        }

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
