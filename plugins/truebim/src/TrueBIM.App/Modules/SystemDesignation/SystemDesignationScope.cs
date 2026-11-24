using Autodesk.Revit.DB;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.SystemDesignation;

internal enum DesignationScope { Selection, View, Project }

internal static class SystemDesignationScope
{
    private static readonly BuiltInCategory[] InsulationCategories =
    [
        BuiltInCategory.OST_DuctInsulations, BuiltInCategory.OST_DuctLinings, BuiltInCategory.OST_PipeInsulations
    ];

    private static readonly HashSet<BuiltInCategory> Categories =
    [
        .. InsulationCategories,
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctFitting,
        BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_PlaceHolderDucts,
        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves, BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_PlaceHolderPipes, BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_FabricationDuctwork, BuiltInCategory.OST_FabricationPipework,
#if REVIT2023_OR_GREATER
        BuiltInCategory.OST_PlumbingEquipment, BuiltInCategory.OST_MechanicalControlDevices,
#endif
    ];

    private static readonly HashSet<BuiltInCategory> ElectricalCategories =
    [
        BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices,
        BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_DataDevices,
        BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_NurseCallDevices,
        BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_TelephoneDevices,
        BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting,
        BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting
    ];

    // The key is the only element eligible for a write. Its value can be a parent outside the scope.
    public static Dictionary<ElementId, Element> Collect(Document doc, DesignationScope scope,
        ICollection<ElementId> selection, View view)
    {
        IEnumerable<Element> candidates;
        if (scope == DesignationScope.Selection)
            candidates = ExpandSelection(doc, selection);
        else
        {
            if (scope == DesignationScope.View && !FilteredElementCollector.IsViewValidForElementIteration(doc, view.Id))
                throw new InvalidOperationException("Revit не поддерживает выбор элементов на этом виде.");
            using FilteredElementCollector collector = scope == DesignationScope.View
                ? new(doc, view.Id) : new(doc);
            using var filter = new LogicalOrFilter(new ElementMulticategoryFilter(Categories.ToList()),
                new ElementClassFilter(typeof(FamilyInstance)));
            candidates = collector.WhereElementIsNotElementType().WherePasses(filter).ToElements();
        }

        Dictionary<ElementId, Element> result = [];
        bool temporary = scope == DesignationScope.View && view.IsTemporaryHideIsolateActive();
        foreach (Element element in candidates)
        {
            if (temporary && !view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, element.Id))
                continue;
            Element? source = ResolveSource(element);
            if (source != null) result[element.Id] = source;
        }
        if (scope == DesignationScope.View && result.Count > 0)
            DesignationViewVisibility.Restrict(doc, view, result);
        return result;
    }

    private static IEnumerable<Element> ExpandSelection(Document doc, ICollection<ElementId> selection)
    {
        var pending = new Queue<ElementId>(selection);
        HashSet<ElementId> visited = [];
        using var insulation = new ElementMulticategoryFilter(InsulationCategories);
        while (pending.Count > 0)
        {
            ElementId id = pending.Dequeue();
            if (!visited.Add(id) || doc.GetElement(id) is not Element element || element is ElementType) continue;
            yield return element;
            if (element is FamilyInstance family)
                foreach (ElementId child in family.GetSubComponentIds()) pending.Enqueue(child);
            if (element is MEPCurve or FamilyInstance or FabricationPart)
                foreach (ElementId child in element.GetDependentElements(insulation)) pending.Enqueue(child);
        }
    }

    private static Element? ResolveSource(Element element)
    {
        bool eligible = false;
        Element current = element;
        while (true)
        {
            if (current.Category is not { CategoryType: CategoryType.Model } category) return null;
            var id = (BuiltInCategory)RevitElementIds.GetValue(category.Id);
            if (ElectricalCategories.Contains(id)) return null;
            eligible |= Categories.Contains(id);
            if (!eligible && current is FamilyInstance instance)
                eligible = SystemDesignationValues.ServiceConnectors(instance).Any();
            if (current is not FamilyInstance { SuperComponent: FamilyInstance parent })
                return eligible ? current : null;
            current = parent;
        }
    }
}
