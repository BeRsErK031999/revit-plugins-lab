using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TrueBIM.App.Modules.RebarFill.Revit;
using TrueBIM.App.Modules.RebarFill.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class RebarFillCommand : IExternalCommand
{
    private const string Title = "Заполнятор арматуры";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument is null || uiDocument.Document.IsFamilyDocument) return Result.Cancelled;
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            ICollection<ElementId> selection = uiDocument.Selection.GetElementIds();
            if (selection.Count == 0)
            {
                if (uiDocument.ActiveView is ViewSchedule)
                {
                    TaskDialog.Show(Title, "Выделите строки арматуры в спецификации и повторите запуск. Если Revit не передаёт строки как выделенные элементы, используйте «Выделить в модели» и запустите команду на плане или 3D-виде.");
                    return Result.Cancelled;
                }
                selection = uiDocument.Selection.PickObjects(ObjectType.Element, new TargetFilter(),
                    "Выберите арматуру или семейства обобщённых моделей и нажмите «Готово».")
                    .Select(reference => reference.ElementId).Distinct().ToList();
            }
            RebarFillService service = new();
            IReadOnlyList<Element> targets = service.ResolveTargets(uiDocument.Document, selection);
            if (targets.Count == 0)
            {
                TaskDialog.Show(Title, "В выделении не найдено арматуры. Выберите стержни (в том числе вложенные) или родительские обобщённые модели. Для армирования по площади выбирайте конкретные стержни, а не всю систему.");
                return Result.Cancelled;
            }
            IReadOnlyList<RebarFillParameter> parameters = service.CollectParameters(targets);
            RebarFillWindow window = new(targets.Count, parameters, option => service.Inspect(targets, option),
                (rows, option, value) => service.ValidateInput(uiDocument.Document, rows, option, value));
            if (RevitModalWindowService.ShowDialog(window, commandData.Application.MainWindowHandle) != true) return Result.Cancelled;
            HashSet<long> checkedIds = new(window.SelectedElementIds);
            targets = targets.Where(element => checkedIds.Contains(Services.RevitElementIds.GetValue(element.Id))).ToList();
            if (targets.Count == 0) return Result.Cancelled;
            RebarFillParameter selected = window.SelectedParameter!;
            bool onlyEmpty = window.OnlyEmpty;
            IReadOnlyList<RebarFillRow> preview = service.Inspect(targets, selected);
            int filled = preview.Count(r => r.CanWrite && r.HasValue);
            if (!onlyEmpty && filled > 0)
            {
                TaskDialog confirm = new(Title)
                {
                    MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                    MainInstruction = $"Параметр «{selected.Name}» уже заполнен у {filled} элементов",
                    MainContent = $"Новое значение: {window.Input}\nВсего доступно для записи: {preview.Count(r => r.CanWrite)}.",
                    CommonButtons = TaskDialogCommonButtons.Cancel, DefaultButton = TaskDialogResult.Cancel
                };
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Перезаписать выбранные", "Заменить существующие значения и заполнить пустые.");
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Заполнить только пустые", "Сохранить уже заполненные значения.");
                TaskDialogResult choice = confirm.Show();
                if (choice == TaskDialogResult.CommandLink2) onlyEmpty = true;
                else if (choice != TaskDialogResult.CommandLink1) return Result.Cancelled;
            }
            IReadOnlyList<RebarFillRow> result = service.Apply(uiDocument.Document, targets, selected, window.Input, onlyEmpty);
            logger.Info($"Rebar fill: targets={targets.Count}, written={result.Count(r => r.Status == "Записано")}, parameter={selected.Key}.");
            RevitModalWindowService.ShowDialog(new RebarFillResultWindow(selected.Name, window.Input, result), commandData.Application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (Exception exception)
        {
            logger.Error("Rebar fill failed.", exception);
            TaskDialog.Show(Title, "Не удалось завершить заполнение.\n\n" + exception.Message);
            return Result.Failed;
        }
    }

    private sealed class TargetFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => RebarFillService.IsReinforcement(element) || RebarFillService.IsGenericModel(element);
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}

public sealed class RebarFillCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        => applicationData.ActiveUIDocument is { Document.IsFamilyDocument: false };
}
