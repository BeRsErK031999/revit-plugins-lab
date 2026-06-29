using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenTrueBimCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ModuleRegistry registry = ModuleRegistry.CreateDefault();
        string moduleList = string.Join(Environment.NewLine, registry.Modules.Select(module => "- " + module.DisplayName));

        TaskDialog.Show(
            "TrueBIM",
            "TrueBIM shell is ready." + Environment.NewLine + Environment.NewLine +
            "Installed modules:" + Environment.NewLine +
            moduleList);

        return Result.Succeeded;
    }
}
