using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.GeometricFinishes;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class FloorFinishingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => GeometricFinishCommand.Execute(data, FinishKind.Floors, ref message);
}
[Transaction(TransactionMode.Manual)]
public sealed class WallFinishingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => GeometricFinishCommand.Execute(data, FinishKind.Walls, ref message);
}
[Transaction(TransactionMode.Manual)]
public sealed class CeilingFinishingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => GeometricFinishCommand.Execute(data, FinishKind.Ceilings, ref message);
}
internal static class GeometricFinishCommand
{
    public static Result Execute(ExternalCommandData data, FinishKind kind, ref string message)
    {
        var logger = new FileTrueBimLogger(new TrueBimLogPaths());
        try
        {
            UIDocument? ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument)
            { TaskDialog.Show(FinishService.Title(kind), "Откройте проект Revit с помещениями."); return Result.Cancelled; }
            if (ui.Document.IsReadOnly)
            { TaskDialog.Show(FinishService.Title(kind), "Проект доступен только для чтения."); return Result.Cancelled; }
            FinishWindow window = new(new FinishCatalog(ui), kind);
            RevitModalWindowService.ShowDialog(window, data.Application.MainWindowHandle);
            if (window.LastResult != null)
            {
                logger.Info(FinishService.Title(kind) + ": " + window.LastResult.Summary);
                foreach (FinishIssue issue in window.LastResult.Issues) logger.Warning(issue.ToString());
            }
            if (window.RequestedRoomIds.Count > 0)
            {
                ui.Selection.SetElementIds(window.RequestedRoomIds.ToList());
                ui.ShowElements(window.RequestedRoomIds.ToList());
            }
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            logger.Error("Geometric finishes command failed: " + kind, ex);
            message = ex.Message;
            TaskDialog.Show(FinishService.Title(kind), "Не удалось выполнить команду: " + ex.Message);
            return Result.Failed;
        }
    }
}
