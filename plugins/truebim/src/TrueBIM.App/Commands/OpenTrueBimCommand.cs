using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenTrueBimCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ModuleRegistry registry = ModuleRegistry.CreateDefault();
        ModuleLauncherWindow window = new(registry.Modules);

        System.Windows.Interop.WindowInteropHelper helper = new(window)
        {
            Owner = commandData.Application.MainWindowHandle
        };

        window.ShowDialog();

        return Result.Succeeded;
    }
}
