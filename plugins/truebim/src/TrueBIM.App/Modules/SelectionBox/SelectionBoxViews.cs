using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TrueBIM.App.Modules.SelectionBox;

public static class SelectionBoxViews
{
    private static readonly ConditionalWeakTable<Document, List<ElementId>> Recent = new();

    public static void Record(View? view)
    {
        if (view is not View3D || view.IsTemplate) return;
        List<ElementId> ids = Recent.GetOrCreateValue(view.Document);
        ids.Remove(view.Id);
        ids.Insert(0, view.Id);
    }

    public static string DefaultName(Document document) => document.IsWorkshared
        ? "{3D - " + document.Application.Username + "}"
        : "{3D}";

    public static View3D? Find(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        if (uiDocument.ActiveView is View3D active && !active.IsTemplate) return active;

        List<ElementId> open = uiDocument.GetOpenUIViews().Select(v => v.ViewId).ToList();
        IEnumerable<ElementId> recent = Recent.TryGetValue(document, out List<ElementId>? ids) ? ids : Enumerable.Empty<ElementId>();
        foreach (ElementId id in recent.Concat(open).Distinct())
            if (open.Contains(id) && document.GetElement(id) is View3D view && !view.IsTemplate)
                return view;

        string name = DefaultName(document);
        return new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

}
