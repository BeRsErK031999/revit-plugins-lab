using Autodesk.Revit.UI;
using TrueBIM.App.Commands;

namespace TrueBIM.App;

public sealed class App : IExternalApplication
{
    private const string TabName = "TrueBIM";
    private const string PanelName = "Tools";

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

        PushButtonData buttonData = new(
            "TrueBIM_Open",
            "TrueBIM",
            typeof(App).Assembly.Location,
            typeof(OpenTrueBimCommand).FullName);

        panel.AddItem(buttonData);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
