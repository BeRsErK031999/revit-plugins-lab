using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    private static void PlanBreakBoundary(DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options)
    {
        // Use the construction and bar envelope, not the crop that this command
        // may expand. Repeating the command must not move the breaks outwards.
        var sections = Sections(frame, geometry, new DetailingOptions { Details = false, Variant = options.Variant });
        var concrete = new DetailingRect(sections.Min(s => s.Wall.Left), sections.Min(s => s.Wall.Bottom),
            sections.Max(s => s.Wall.Right), sections.Max(s => Math.Max(s.Wall.Top,
                s.UpperSlabs.Select(h => h.Bounds.Top).DefaultIfEmpty(s.Wall.Top).Max())));
        var bars = new DetailingRect(sections.Min(s => s.Reinforcement.Left), sections.Min(s => s.Reinforcement.Bottom),
            sections.Max(s => s.Reinforcement.Right), sections.Max(s => s.Reinforcement.Top));
        geometry.PlannedBreakBoundary = DetailingBoundsPlan.Around(concrete, bars, 250 / 304.8);
        geometry.Breaks(frame);
    }

    internal static IReadOnlyList<XYZ> BreakGraphicPoints(FamilyInstance detail, View view)
    {
        var points = new List<XYZ>();
        GeometryElement geometry = detail.get_Geometry(new Options { View = view, IncludeNonVisibleObjects = false });
        if (geometry != null) Read(geometry, Transform.Identity);
        return points;

        void Read(GeometryElement elements, Transform transform)
        {
            foreach (GeometryObject element in elements)
            {
                if (element is GeometryInstance instance) Read(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform));
                else if (element is Curve curve && curve.IsBound)
                    points.AddRange(curve.Tessellate().Select(transform.OfPoint));
            }
        }
    }

    private static void FitModelCropToBreaks(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry)
    {
        var breaks = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Symbol.FamilyName.StartsWith("(Узел) Область обрыва", StringComparison.Ordinal)).ToList();
        var points = breaks.SelectMany(b => BreakGraphicPoints(b, view)).ToList();
        if (points.Count == 0)
        {
            // A family without exposed curves still needs room for its zigzag.
            foreach (var p in geometry.Breaks(frame).SelectMany(p => p))
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
                points.Add(frame.Point(p.X + x * frame.Paper(4), p.Y + y * frame.Paper(4)));
        }
        if (points.Count == 0) return;
        BoundingBoxXYZ crop = view.CropBox;
        var local = points.Select(crop.Transform.Inverse.OfPoint).ToList();
        var current = new DetailingRect(crop.Min.X, crop.Min.Y, crop.Max.X, crop.Max.Y);
        var graphics = new DetailingRect(local.Min(p => p.X), local.Min(p => p.Y), local.Max(p => p.X), local.Max(p => p.Y));
        var expanded = DetailingBoundsPlan.Expand(current, graphics, 30 / 304.8);
        if (Math.Abs(expanded.Left - current.Left) + Math.Abs(expanded.Right - current.Right)
            + Math.Abs(expanded.Bottom - current.Bottom) + Math.Abs(expanded.Top - current.Top) < 0.01 / 304.8) return;
        using ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();
        if (manager.ShapeSet || manager.NumberOfSplitRegions > 1)
            throw new InvalidOperationException("Для полного отображения обрывов нужна прямоугольная неразделённая обрезка вида.");
        crop.Min = new XYZ(expanded.Left, expanded.Bottom, crop.Min.Z);
        crop.Max = new XYZ(expanded.Right, expanded.Top, crop.Max.Z);
        view.CropBox = crop;
        doc.Regenerate();
    }

    private static void AlignDetailNotes(Document doc, View view, DetailingFrame frame, DetailingOptions options)
    {
        BoundingBoxXYZ crop = view.CropBox;
        double right = Math.Max(frame.X(crop.Transform.OfPoint(crop.Min)), frame.X(crop.Transform.OfPoint(crop.Max)));
        foreach (AnnotationSymbol note in new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance))
            .OfType<AnnotationSymbol>().Where(n => options.Details && DetailRole(n) == "note" || options.Pins && DetailRole(n) == "pin-note"))
        {
            var leaders = note.GetLeaders();
            if (leaders.Count == 0) continue;
            var ends = leaders.Select(l => l.End).ToList();
            double offset = right - leaders.Min(l => frame.X(l.Anchor));
            ElementTransformUtils.MoveElement(doc, note.Id, frame.Right * offset);
            doc.Regenerate();
            leaders = note.GetLeaders();
            for (int index = 0; index < leaders.Count; index++)
            {
                leaders[index].End = ends[index];
                leaders[index].Elbow = leaders[index].Anchor;
            }
        }
    }
}
