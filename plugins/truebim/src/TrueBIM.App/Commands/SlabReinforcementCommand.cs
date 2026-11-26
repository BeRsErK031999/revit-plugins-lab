#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TrueBIM.App.Modules.SlabReinforcement;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class SlabReinforcementCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var ui = data.Application.ActiveUIDocument;
        if (ui == null || ui.Document.IsFamilyDocument)
            return Result.Cancelled;
        try
        {
            var doc = ui.Document;
            var sel = ui.Selection.GetElementIds();
            var floor = sel.Count == 1 ? doc.GetElement(sel.Single()) as Floor : null;
            floor ??= (Floor)doc.GetElement(ui.Selection.PickObject(ObjectType.Element, new FloorFilter(), "Выберите одно железобетонное перекрытие"));
            if (RebarHostData.GetRebarHostData(floor) == null)
                throw new InvalidOperationException("Выбранная плита не поддерживает армирование.");
            var owned = SlabReinforcementService.Owned(floor);
            var manual = SlabReinforcementService.Manual(floor, new HashSet<ElementId>(owned.Select(e => e.Id)));
            int oldFrames = owned.OfType<FamilyInstance>().Count();
            var window = new SlabWindow(floor, manual.Count, oldFrames);
            if (RevitModalWindowService.ShowDialog(window, data.Application.MainWindowHandle) != true)
                return Result.Cancelled;
            var options = window.Settings!;
            var first = PickEdge(ui, floor, "Укажите прямую кромку или боковую грань вдоль буквенных осей");
            Line? second = null;
            if (options.PlaceFrames && (oldFrames == 0 || options.ReplaceFrames))
                second = PickEdge(ui, floor, "Укажите вторую сторону угла начала сетки каркасов вдоль цифровых осей");
            var result = SlabReinforcementService.Apply(floor, first.Direction, first, second, options);
            var report = new TaskDialog("Армирование плиты")
            {
                MainInstruction = $"Плита {floor.Id} · создано областей: {result.Areas}",
                MainContent = $"Торцевых стержней: {result.EndBars}. Каркасов на плите: {result.Frames}. Заменено элементов: {result.Deleted}."};
            if (result.Warnings.Count > 0)
                report.MainContent += "\n\n" + string.Join("\n", result.Warnings);
            if (result.ReviewBars.Count > 0)
            {
                report.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                report.MainInstruction = $"Армирование плиты {floor.Id} · требуется проверка торцов";
                report.ExpandedContent = "ID фоновых стержней для проверки: " + string.Join(", ", result.ReviewBars.OrderBy(id => id.Value));
                report.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Выделить ряды с пропущенными торцами");
            }

            if (report.Show() == TaskDialogResult.CommandLink1)
                ui.Selection.SetElementIds(result.ReviewBars);
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    private static Line PickEdge(UIDocument ui, Floor floor, string prompt)
    {
        var faces = HostObjectUtils.GetTopFaces(floor);
        if (faces.Count != 1 || floor.GetGeometryObjectFromReference(faces[0]) is not PlanarFace face)
            throw new InvalidOperationException("Выберите плиту с одной плоской верхней гранью.");
        var normal = face.FaceNormal;
        var filter = new EdgeFilter(floor, normal);
        var reference = ui.Selection.PickObject(ObjectType.PointOnElement, filter, prompt);
        return filter.Resolve(reference) ?? throw new InvalidOperationException("Выберите прямую сторону этой плиты.");
    }

    private sealed class FloorFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Floor;
        public bool AllowReference(Reference r, XYZ p) => false;
    }

    private sealed class EdgeFilter(Floor floor, XYZ normal) : ISelectionFilter
    {
        public bool AllowElement(Element e) => e.Id == floor.Id;
        public bool AllowReference(Reference r, XYZ p) => Resolve(r) != null;
        internal Line? Resolve(Reference r)
        {
            if (r.ElementId != floor.Id)
                return null;
            var obj = floor.GetGeometryObjectFromReference(r);
            if (obj is Edge e && e.AsCurve()is Line line && Math.Abs(line.Direction.DotProduct(normal)) < .001)
                return line;
            if (obj is PlanarFace side && Math.Abs(side.FaceNormal.DotProduct(normal)) < .5)
                return side.GetEdgesAsCurveLoops().SelectMany(l => l).OfType<Line>().Where(l => Math.Abs(l.Direction.DotProduct(normal)) < .001).OrderByDescending(l => l.Length).FirstOrDefault();
            return null;
        }
    }
}
#endif
