#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.SlabReinforcement;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class SlabOpeningsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var ui = data.Application.ActiveUIDocument;
        if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
        ElementId? floorId = null;
        try
        {
            var floor = SlabReinforcementCommand.PickFloor(ui);
            floorId = floor.Id;
            var edge = SlabReinforcementCommand.PickEdge(ui, floor, "Выделите грань плиты вдоль буквенных осей для обрамления отверстий");
            var geometry = new SlabGeometry(floor, edge.Direction);
            var window = new SlabOpeningWindow(floor, SlabOpeningService.ReadLayers(floor, geometry));
            if (RevitModalWindowService.ShowDialog(window, data.Application.MainWindowHandle) != true)
                return Result.Cancelled;
            var result = SlabOpeningService.Apply(floor, geometry, window.Settings!);
            var report = new TaskDialog("Обрамление отверстий")
            {
                MainInstruction = $"Плита {floor.Id} · учтено отверстий и групп: {result.Groups}",
                MainContent = $"Форма 000: {result.Straight}. Форма 200: {result.Bends}. Заменено прежних стержней: {result.Replaced}."
            };
            if (result.Warnings.Count > 0)
            {
                report.MainContent += $"\nТребуют решения проектировщика: {result.Warnings.Count} рядов. См. подробности.";
                report.ExpandedContent = string.Join("\n", result.Warnings);
            }
            report.Show();
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (Exception ex)
        {
            message = ex.Message;
            try
            {
                var logger = new FileTrueBimLogger(new TrueBimLogPaths());
                logger.Error($"Ошибка обрамления отверстий. ID плиты: {floorId?.ToString() ?? "не выбрана"}.", ex);
            }
            catch (Exception logError) when (logError is System.IO.IOException or UnauthorizedAccessException)
            {
                message += "\nНе удалось записать подробности в журнал TrueBIM.";
            }
            return Result.Failed;
        }
    }
}
#endif
