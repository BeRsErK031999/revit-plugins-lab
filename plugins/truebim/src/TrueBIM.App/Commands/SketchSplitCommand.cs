using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
#if REVIT2022_OR_GREATER
using TrueBIM.App.Modules.SketchSplit;
#endif

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class SketchSplitCommand : IExternalCommand
{
    private const string Title = "Разделить перекрытия и потолки";

    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
#if REVIT2022_OR_GREATER
        var ui = data.Application.ActiveUIDocument;
        if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
        var doc = ui.Document;
        var selected = ui.Selection.GetElementIds();
        var scope = new TaskDialog(Title) { MainInstruction = "Где искать элементы с раздельными участками?", CommonButtons = TaskDialogCommonButtons.Cancel };
        scope.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Во всём проекте");
        if (selected.Count > 0) scope.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"В выделении ({selected.Count})");
        var choice = scope.Show();
        if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2) return Result.Cancelled;
        var candidates = choice == TaskDialogResult.CommandLink1
            ? new FilteredElementCollector(doc).WherePasses(new ElementMulticlassFilter(new[] { typeof(Floor), typeof(Ceiling) })).WhereElementIsNotElementType().ToElements()
            : selected.Select(doc.GetElement).Where(e => e is Floor or Ceiling).ToList();
        var service = new SketchSplitService();
        var found = new List<(Element Element, int Count)>();
        var issues = new List<string>();
        foreach (var e in candidates)
        {
            try { int count = service.CountParts(e); if (count > 1) found.Add((e, count)); }
            catch (Exception ex) { issues.Add($"ID {e.Id}: {ex.Message}"); }
        }
        if (found.Count == 0)
        {
            TaskDialog.Show(Title, "Раздельных участков эскиза не найдено." + Details(issues));
            return Result.Succeeded;
        }
        var preview = new TaskDialog(Title)
        {
            MainInstruction = $"Найдено элементов: {found.Count}. После разбиения участков: {found.Sum(x => x.Count)}.",
            MainContent = "Отверстия сохраняются в своём участке. Для каждого элемента команда проверит размеры, марки, геометрию и зависимые объекты.",
            ExpandedContent = string.Join("\n", found.Select(x => $"ID {x.Element.Id} · {x.Element.Name} · участков {x.Count}" +
                (x.Element.Pinned ? " · закреплён" : "") + (x.Element.GroupId != ElementId.InvalidElementId ? $" · группа {x.Element.GroupId}" : ""))) + Details(issues),
            CommonButtons = TaskDialogCommonButtons.Cancel
        };
        preview.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Разделить найденные элементы");
        if (preview.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;
        bool allowGroup = false, allowPin = false;
        if (found.Any(x => x.Element.GroupId != ElementId.InvalidElementId))
        {
            var grouped = Protection("Элементы в группах", "Пропустить элементы групп",
                "Разгруппировать затронутые экземпляры групп", "Остальные участники этих экземпляров также выйдут из группы.");
            if (grouped == TaskDialogResult.Cancel) return Result.Cancelled;
            allowGroup = grouped == TaskDialogResult.CommandLink2;
        }
        if (found.Any(x => x.Element.Pinned))
        {
            var pinned = Protection("Закреплённые элементы", "Пропустить закреплённые элементы",
                "Разделить и закрепить все полученные части", "Закрепление будет восстановлено на каждой части.");
            if (pinned == TaskDialogResult.Cancel) return Result.Cancelled;
            allowPin = pinned == TaskDialogResult.CommandLink2;
        }
        var newIds = new List<ElementId>(); int processed = 0;
        foreach (var item in found)
        {
            var id = item.Element.Id;
            try { newIds.AddRange(service.Split(doc, id, allowPin, allowGroup)); processed++; }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { issues.Add($"ID {id}: {ex.Message}"); }
        }
        ui.Selection.SetElementIds(newIds);
        new TaskDialog(Title)
        {
            MainInstruction = $"Разделено элементов: {processed}. Получено частей: {newIds.Count}.",
            MainContent = issues.Count == 0 ? "Оформление и геометрия проверены." : $"Замечаний: {issues.Count}. Подробности содержат ID и причину пропуска.",
            ExpandedContent = string.Join("\n", issues)
        }.Show();
        return Result.Succeeded;
#else
        TaskDialog.Show(Title,"Редактирование эскизов этой командой доступно с Revit 2022.");
        return Result.Cancelled;
#endif
    }
#if REVIT2022_OR_GREATER
    private static string Details(List<string> items) => items.Count == 0 ? "" : "\n" + string.Join("\n", items);
    private static TaskDialogResult Protection(string title, string skip, string process, string info)
    {
        var dialog = new TaskDialog(Title) { MainInstruction = title, MainContent = info, CommonButtons = TaskDialogCommonButtons.Cancel };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, skip);
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, process);
        return dialog.Show();
    }
#endif
}
