using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitOperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;

public static class FamilyReplacementWorkflow
{
    public static Result Run(UIApplication application, long? preferredTargetTypeId = null)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            UIDocument? uiDocument = application.ActiveUIDocument;
            if (uiDocument is null || uiDocument.Document.IsFamilyDocument)
            {
                TaskDialog.Show("Замена семейств", "Откройте проект Revit для замены экземпляров семейств.");
                return Result.Cancelled;
            }

            Document document = uiDocument.Document;
            if (document.IsReadOnly)
            {
                TaskDialog.Show("Замена семейств", "Текущий проект доступен только для чтения.");
                return Result.Cancelled;
            }

            FamilyReplacementCollector collector = new();
            IReadOnlyList<long> selectedIds = uiDocument.Selection.GetElementIds().Select(RevitElementIds.GetValue).ToList();
            IReadOnlyList<FamilyReplacementViewOption> views = collector.CollectViews(document, selectedIds);
            int selectedInstanceCount = selectedIds.Count(id => document.GetElement(RevitElementIds.Create(id)) is FamilyInstance);
            FamilyReplacementScopeWindow scope = new(views, document.ActiveView.Name, selectedInstanceCount);
            if (RevitModalWindowService.ShowDialog(scope, application.MainWindowHandle) != true)
            {
                return Result.Cancelled;
            }

            if (scope.Scope == FamilyReplacementScope.PickElements)
            {
                selectedIds = uiDocument.Selection.PickObjects(ObjectType.Element, new FamilyInstanceFilter(),
                    "Выберите исходные экземпляры семейств и нажмите «Готово».")
                    .Select(reference => RevitElementIds.GetValue(reference.ElementId)).Distinct().ToList();
            }

            IReadOnlyList<FamilyReplacementCandidate> candidates = collector.Collect(document, scope.Scope,
                document.ActiveView.Id, selectedIds, scope.SelectedViewIds);
            if (candidates.Count == 0)
            {
                TaskDialog.Show("Замена семейств", "В выбранной области не найдено экземпляров модельных семейств.");
                return Result.Cancelled;
            }

            IReadOnlyList<FamilyReplacementTypeOption> targets = collector.CollectTargets(document);
            if (targets.Count == 0)
            {
                TaskDialog.Show("Замена семейств", "В проекте нет загруженных типов модельных семейств для замены.");
                return Result.Cancelled;
            }

            FamilyReplacementSelectionWindow selection = new(candidates, targets, collector.CollectTagTypes(document),
                ScopeLabel(scope.Scope, scope.SelectedViewIds.Count), preferredTargetTypeId);
            if (RevitModalWindowService.ShowDialog(selection, application.MainWindowHandle) != true
                || selection.TargetTypeId is not long targetTypeId)
            {
                return Result.Cancelled;
            }

            FamilyReplacementResult result = FamilyReplacementSelectionRunner.Replace(
                uiDocument, selection.SourceIds, targetTypeId, selection.Alignment, selection.TargetTagTypeId,
                exception => logger.Warning($"Family replacement finished, but canvas selection could not be restored: {exception.Message}"));
            logger.Info($"Family replacement: {result.Summary}");
            foreach (FamilyReplacementItemResult item in result.Items.Where(item => !item.Replaced))
            {
                logger.Warning($"Family replacement source {item.SourceId}: {item.Message}");
            }

            RevitModalWindowService.ShowDialog(new FamilyReplacementReportWindow(result), application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (RevitOperationCanceledException)
        {
            logger.Info("Family replacement cancelled during element selection.");
            return Result.Cancelled;
        }
        catch (Exception exception)
        {
            logger.Error("Family replacement failed.", exception);
            TaskDialog.Show("Замена семейств", $"Не удалось выполнить замену.\n{exception.Message}");
            return Result.Failed;
        }
    }

    private static string ScopeLabel(FamilyReplacementScope scope, int views) => scope switch
    {
        FamilyReplacementScope.ActiveView => "На активном виде",
        FamilyReplacementScope.SelectedViews => $"На выбранных видах ({views})",
        FamilyReplacementScope.Project => "Во всём проекте",
        FamilyReplacementScope.CurrentSelection => "Предварительно выделенные экземпляры",
        _ => "Экземпляры, выбранные в модели"
    };

    private sealed class FamilyInstanceFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => element is FamilyInstance
            && element.Category?.CategoryType == CategoryType.Model;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
