using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.RebarDetailing;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class RebarDetailingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? ui = commandData.Application.ActiveUIDocument;
        if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            RebarDetailingService service = new();
            var views = service.ResolveViews(ui.Document, ui.Selection.GetElementIds(), ui.ActiveView);
            if (views.Count == 0)
            {
                TaskDialog.Show("Оформление сечений", "Выберите виды на листе или в диспетчере проекта. Для оформления открытого разреза или плана снимите выделение и повторите команду.");
                return Result.Cancelled;
            }
            RebarDetailingWindow window = new(ui.Document, service.Inspect(views), service);
            if (RevitModalWindowService.ShowDialog(window, commandData.Application.MainWindowHandle) != true) return Result.Cancelled;
            var results = service.Apply(ui.Document, window.SelectedViews, window.Options);
            string report = string.Join("\n\n", results.Select(r => r.ToString()));
            logger.Info("Rebar detailing:\n" + report);
            new TaskDialog("Оформление сечений") { MainInstruction = $"Оформлено видов: {results.Count(r => r.Succeeded)} из {results.Count}",
                MainContent = "Подробный результат доступен ниже. Изменения можно отменить одной командой Revit.", ExpandedContent = report, CommonButtons = TaskDialogCommonButtons.Close }.Show();
            return results.Any(r => r.Succeeded) ? Result.Succeeded : Result.Cancelled;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (Exception ex) { logger.Error("Rebar detailing failed.", ex); TaskDialog.Show("Оформление сечений", ex.Message); return Result.Failed; }
    }
}

public sealed class RebarDetailingCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => applicationData.ActiveUIDocument is { Document.IsFamilyDocument: false };
}
