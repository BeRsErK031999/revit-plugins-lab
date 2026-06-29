using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Modules.SheetNumbering.UI;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenTrueBimCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ModuleRegistry registry = ModuleRegistry.CreateDefault();
        Document? activeDocument = commandData.Application.ActiveUIDocument?.Document;
        Dictionary<string, Action<System.Windows.Window>> moduleActions = new()
        {
            ["truebim.sheet-numbering"] = owner =>
            {
                if (activeDocument is null)
                {
                    TaskDialog.Show("Sheet Numbering", "Open a Revit document before starting Sheet Numbering.");
                    return;
                }

                IReadOnlyList<SheetInfo> sheets = new SheetCollectorService().Collect(activeDocument);
                SheetNumberingWindow sheetNumberingWindow = new(
                    sheets,
                    new SheetNumberingPreviewWorkflow(
                        new SheetNumberPreviewService(),
                        new DuplicateSheetNumberDetector()))
                {
                    Owner = owner
                };
                sheetNumberingWindow.ShowDialog();
            }
        };

        ModuleLauncherWindow window = new(registry.Modules, moduleActions);

        System.Windows.Interop.WindowInteropHelper helper = new(window)
        {
            Owner = commandData.Application.MainWindowHandle
        };

        window.ShowDialog();

        return Result.Succeeded;
    }
}
