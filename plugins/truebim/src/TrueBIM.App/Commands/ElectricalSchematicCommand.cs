using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.ElectricalSchematic.Services;
using TrueBIM.App.Modules.ElectricalSchematic.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ElectricalSchematicCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show("Электрическая схема", "Откройте проект Revit для обновления электрической схемы.");
            return Result.Cancelled;
        }
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            ElectricalSchematicService service = new(uiDocument.Document);
            ElectricalSchematicWindow window = new(service.Preview, service.Apply,
                uiDocument.Selection.GetElementIds().Select(ElectricalSchematicService.Id), logger);
            RevitModalWindowService.ShowDialog(window, commandData.Application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Electrical schematic command failed.", exception);
            message = exception.Message;
            return Result.Failed;
        }
    }
}
