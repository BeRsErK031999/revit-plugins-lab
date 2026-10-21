using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Modules.RebarSplit.Revit;
using TrueBIM.App.Modules.RebarSplit.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class RebarSplitCommand : IExternalCommand
{
    private const string Title = "Разбить арматуру";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument is null || uiDocument.Document.IsFamilyDocument) return Result.Cancelled;
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        ICollection<ElementId> originalSelection = uiDocument.Selection.GetElementIds();
        try
        {
            RebarSplitWindow window = new(originalSelection.Count);
            if (RevitModalWindowService.ShowDialog(window, commandData.Application.MainWindowHandle) != true) return Result.Cancelled;
            RebarSplitOptions options = window.Options;
            ICollection<ElementId> selection = originalSelection;
            if (options.Scope == RebarSplitScope.Selection && selection.Count == 0)
                selection = uiDocument.Selection.PickObjects(ObjectType.Element, new SetSelectionFilter(), "Выберите наборы арматуры и нажмите «Готово».")
                    .Select(reference => reference.ElementId).Distinct().ToList();
            RebarSplitService service = new(text => logger.Info(text));
            IReadOnlyList<ElementId> candidates = service.FindCandidates(uiDocument.Document, selection, options.Scope);
            if (candidates.Count == 0)
            {
                TaskDialog.Show(Title, options.Scope == RebarSplitScope.Selection
                    ? "В выбранных элементах нет обычных наборов арматуры с несколькими стержнями."
                    : "В проекте нет обычных наборов с разницей фактических длин стержней более 0,1 мм.");
                return Result.Succeeded;
            }
            // Снимаем UI-выделение, чтобы служебные элементы выбора не участвовали в каскадном удалении.
            uiDocument.Selection.SetElementIds(Array.Empty<ElementId>());
            RebarSplitResult result = service.Execute(uiDocument.Document, candidates, options.PreserveAnnotations, ResolveParameters);
            uiDocument.Selection.SetElementIds(result.CreatedIds.Concat(originalSelection.Where(id => uiDocument.Document.GetElement(id) is not null)).ToList());
            logger.Info($"Rebar split: cancelled={result.Cancelled}, sets={result.SetCount}, bars={result.CreatedIds.Count}, skipped={result.SkippedCount}.");
            if (result.Cancelled) return Result.Cancelled;
            TaskDialog.Show(Title, $"Разбито наборов: {result.SetCount}.\nСоздано отдельных стержней: {result.CreatedIds.Count}."
                + (result.SkippedCount > 0 ? $"\nПропущено по вашему выбору: {result.SkippedCount}." : string.Empty));
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            RestoreSelection();
            return Result.Cancelled;
        }
        catch (Exception exception)
        {
            RestoreSelection();
            logger.Error("Rebar split failed; operation rolled back.", exception);
            TaskDialog.Show(Title, $"Не удалось выполнить разбиение. Изменения операции отменены.\n\n{exception.Message}");
            return Result.Failed;
        }

        void RestoreSelection() => uiDocument.Selection.SetElementIds(originalSelection.Where(id => uiDocument.Document.GetElement(id) is not null).ToList());
    }

    private static RebarSplitParameterAction ResolveParameters(IReadOnlyList<RebarSplitParameterIssue> issues)
    {
        TaskDialog dialog = new(Title)
        {
            MainIcon = TaskDialogIcon.TaskDialogIconWarning,
            MainInstruction = "Не удалось сохранить все параметры экземпляров",
            MainContent = "ID исходных наборов: " + string.Join(", ", issues.Select(issue => issue.SourceId))
                + "\nВыбранное действие применяется ко всем перечисленным наборам.",
            ExpandedContent = string.Join("\n\n", issues.Select(issue => $"ID {issue.SourceId}\n" + string.Join("\n", issue.Parameters))),
            CommonButtons = TaskDialogCommonButtons.Cancel,
            DefaultButton = TaskDialogResult.Cancel,
            FooterText = "«Отмена» полностью отменяет разбиение всех наборов."
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Пропустить", "Оставить проблемные наборы без изменений и разбить остальные.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Всё равно разбить", "Разбить также проблемные наборы. Часть параметров может не сохраниться.");
        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => RebarSplitParameterAction.Skip,
            TaskDialogResult.CommandLink2 => RebarSplitParameterAction.SplitAnyway,
            _ => RebarSplitParameterAction.Cancel
        };
    }

    private sealed class SetSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => RebarSplitGeometry.IsSupportedSet(element);
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
