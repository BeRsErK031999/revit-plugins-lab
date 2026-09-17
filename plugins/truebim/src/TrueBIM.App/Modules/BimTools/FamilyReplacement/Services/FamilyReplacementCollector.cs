using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;

public sealed class FamilyReplacementCollector
{
    public IReadOnlyList<FamilyReplacementTypeOption> CollectTagTypes(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(symbol => symbol.Category?.IsTagCategory == true)
            .Select(symbol => new FamilyReplacementTypeOption(
                RevitElementIds.GetValue(symbol.Id), RevitElementIds.GetValue(symbol.Category.Id),
                symbol.Category.Name, symbol.FamilyName, symbol.Name, false))
            .OrderBy(symbol => symbol.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(symbol => symbol.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<FamilyReplacementViewOption> CollectViews(Document document, IReadOnlyList<long> selectedIds)
    {
        HashSet<long> selected = new(selectedIds);
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate
                && view.ViewType != ViewType.DrawingSheet
                && view.ViewType != ViewType.Schedule
                && FilteredElementCollector.IsViewValidForElementIteration(document, view.Id))
            .Select(view => new FamilyReplacementViewOption(
                RevitElementIds.GetValue(view.Id), view.Name, view.ViewType.ToString(),
                selected.Contains(RevitElementIds.GetValue(view.Id))))
            .OrderBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<FamilyReplacementTypeOption> CollectTargets(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(symbol => symbol.Category?.CategoryType == CategoryType.Model && !symbol.Family.IsInPlace)
            .Select(symbol => new FamilyReplacementTypeOption(
                RevitElementIds.GetValue(symbol.Id),
                RevitElementIds.GetValue(symbol.Category.Id),
                symbol.Category.Name, symbol.FamilyName, symbol.Name,
                RevitElementIds.GetValue(symbol.Category.Id) == (long)BuiltInCategory.OST_NurseCallDevices))
            .OrderBy(symbol => symbol.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(symbol => symbol.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(symbol => symbol.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<FamilyReplacementCandidate> Collect(
        Document document,
        FamilyReplacementScope scope,
        ElementId activeViewId,
        IReadOnlyList<long> selectedIds,
        IReadOnlyList<long> viewIds)
    {
        IEnumerable<FamilyInstance> instances;
        switch (scope)
        {
            case FamilyReplacementScope.CurrentSelection:
            case FamilyReplacementScope.PickElements:
                instances = selectedIds.Distinct()
                    .Select(id => document.GetElement(RevitElementIds.Create(id)))
                    .OfType<FamilyInstance>();
                break;
            case FamilyReplacementScope.ActiveView:
                instances = CollectInView(document, activeViewId);
                break;
            case FamilyReplacementScope.SelectedViews:
                instances = viewIds.Distinct()
                    .SelectMany(id => CollectInView(document, RevitElementIds.Create(id)))
                    .GroupBy(instance => RevitElementIds.GetValue(instance.Id))
                    .Select(group => group.First());
                break;
            default:
                instances = new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();
                break;
        }

        return instances
            .Where(instance => instance.Category?.CategoryType == CategoryType.Model)
            .Select(instance => new FamilyReplacementCandidate(
                RevitElementIds.GetValue(instance.Id),
                RevitElementIds.GetValue(instance.Symbol.Id),
                RevitElementIds.GetValue(instance.Category.Id),
                instance.Category.Name, instance.Symbol.FamilyName, instance.Symbol.Name,
                (document.GetElement(instance.LevelId) as Level)?.Name ?? "—",
                instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty,
                RevitElementIds.GetValue(instance.Category.Id) == (long)BuiltInCategory.OST_FireAlarmDevices))
            .OrderBy(instance => instance.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(instance => instance.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(instance => instance.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(instance => instance.Id)
            .ToList();
    }

    private static IEnumerable<FamilyInstance> CollectInView(Document document, ElementId viewId)
    {
        if (document.GetElement(viewId) is not View view || view.IsTemplate
            || view.ViewType == ViewType.DrawingSheet || view.ViewType == ViewType.Schedule
            || !FilteredElementCollector.IsViewValidForElementIteration(document, viewId))
        {
            throw new InvalidOperationException("Выберите план, разрез, фасад или 3D-вид модели для поиска экземпляров.");
        }

        return new FilteredElementCollector(document, viewId)
            .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
    }
}
