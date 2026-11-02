using Autodesk.Revit.DB;
using TrueBIM.App.Modules.RebarDetailing;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication
{
    private object CheckSupplement5Options(Document doc, View view, RebarDetailingService service, DetailingVariant variant)
    {
        string original = AnnotationFingerprint(view);
        using TransactionGroup scenario = new(doc, "Disabled callouts and bar-only dimensions"); scenario.Start();
        var notes = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance))
            .OfType<AnnotationSymbol>().Where(RebarDetailingService.OwnedDetail).ToList();
        using (Transaction move = new(doc, "Place callouts manually for option test"))
        {
            move.Start();
            foreach (var note in notes) ElementTransformUtils.MoveElement(doc, note.Id, view.RightDirection * (100 / 304.8));
            move.Commit();
        }
        string NotePositions() => string.Join("|", notes.SelectMany(n => n.GetLeaders()).Select(l =>
            string.Join(",", new[] { l.Anchor.X, l.Anchor.Y, l.Anchor.Z, l.Elbow.X, l.Elbow.Y, l.Elbow.Z, l.End.X, l.End.Y, l.End.Z })));
        string before = NotePositions();
        DetailingOptions options = new()
        {
            Variant = variant, Details = false, Pins = false, ConcreteDimensions = false,
            BarDimensions = true, Tags = false, Elevations = false, WriteQuantities = false,
            DimensionTypeId = service.PreferredType(new[] { view }, service.Types(doc, "dimension"), "dimension")
        };
        var result = service.Apply(doc, new[] { view }, options).Single();
        bool notesPreserved = notes.Count > 0 && NotePositions() == before;
        object measured = MeasureSupplement5(doc, view, options);
        bool upperReferenceSafe = (bool)measured.GetType().GetProperty("UpperReferenceSafe")!.GetValue(measured)!;
        scenario.RollBack();
        bool rollbackSafe = AnnotationFingerprint(view) == original;
        return new { Passed = result.Succeeded && notesPreserved && upperReferenceSafe && rollbackSafe,
            view.Name, Result = result, NotesPreserved = notesPreserved, UpperReferenceSafe = upperReferenceSafe, RollbackSafe = rollbackSafe };
    }

    private static double[] CropCoordinates(View view) => new[] { view.CropBox.Min.X, view.CropBox.Min.Y,
        view.CropBox.Max.X, view.CropBox.Max.Y, view.CropBox.Min.Z, view.CropBox.Max.Z };

    private static bool CropExpandedOnly(double[] before, double[] after) => after[0] <= before[0] + 1e-8
        && after[1] <= before[1] + 1e-8 && after[2] >= before[2] - 1e-8 && after[3] >= before[3] - 1e-8
        && after[4] == before[4] && after[5] == before[5];

    private object MeasureSupplement5(Document doc, View view, DetailingOptions options)
    {
        var frame = new DetailingFrame(view);
        var geometry = DetailingGeometry.Read(doc, view);
        var owned = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().ToList();
        var crop = view.CropBox;
        var points = owned.OfType<FamilyInstance>().Where(f => f.Symbol.FamilyName.StartsWith("(Узел) Область обрыва", StringComparison.Ordinal))
            .SelectMany(f => RebarDetailingService.BreakGraphicPoints(f, view)).Select(crop.Transform.Inverse.OfPoint).ToList();
        double margin = points.Count == 0 ? -1 : points.Min(p => Math.Min(Math.Min(p.X - crop.Min.X, crop.Max.X - p.X),
            Math.Min(p.Y - crop.Min.Y, crop.Max.Y - p.Y))) * 304.8;
        double right = frame.X(crop.Transform.OfPoint(crop.Max));
        var notes = owned.OfType<AnnotationSymbol>().Where(RebarDetailingService.OwnedDetail).SelectMany(n => n.GetLeaders())
            .Select(l => new { AnchorX = frame.X(l.Anchor), ErrorMm = Math.Abs(frame.X(l.Anchor) - right) * 304.8 }).ToList();
        var upperAxes = geometry.Bars.Where(b => b.Link == null && b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999
                && s.Start.DistanceTo(s.End) > 500 / 304.8))
            .SelectMany(b => options.Variant == DetailingVariant.Straight ? b.EndReferences : b.TopReferences)
            .OrderByDescending(a => frame.Y(a.Point)).ToList();
        var top = upperAxes.FirstOrDefault();
        var upperCandidates = top == null ? new List<string>() : upperAxes.Where(a => Math.Abs(frame.Y(a.Point) - frame.Y(top.Point)) < 0.1 / 304.8)
            .SelectMany(a => a.Candidates).Select(GeometryKey).Distinct().ToList();
        var upperDimensions = owned.OfType<Dimension>().Where(d => d is not SpotDimension && d.Curve is Line line
            && Math.Abs(line.Direction.DotProduct(frame.Up)) > 0.999 && d.References.Cast<Reference>()
                .Any(r => upperCandidates.Contains(GeometryKey(r))))
            .Select(d => new { Id = d.Id.Value, ValuesMm = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>()
                .Select(s => (s.Value ?? -1) * 304.8).ToList() : new List<double> { (d.Value ?? -1) * 304.8 } }).ToList();
        var sections = (List<DetailingSection>)typeof(RebarDetailingService)
            .GetMethod("Sections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { frame, geometry, options })!;
        var upperSlabs = sections.SelectMany(s => s.UpperSlabs).ToList();
        double upperConcreteY = upperSlabs.Count > 0 ? upperSlabs.Max(s => s.Bounds.Top) : sections.Max(s => s.Wall.Top);
        double? expectedUpperMm = top == null ? null : Math.Abs(upperConcreteY - frame.Y(top.Point)) * 304.8;
        bool upperSafe = upperDimensions.Count > 0 && upperDimensions.All(d => d.ValuesMm.All(v => v > 0))
            && expectedUpperMm.HasValue && upperDimensions.SelectMany(d => d.ValuesMm).Any(v => Math.Abs(v - expectedUpperMm.Value) < 0.1);
        return new { Passed = margin >= 29.9 && notes.Count > 0 && notes.All(n => n.ErrorMm < 0.1) && upperSafe,
            GraphicPoints = points.Count, BreakGraphicMarginMm = margin, NoteAnchors = notes,
            UpperReferenceSafe = upperSafe, UpperBaseline = upperSlabs.Count > 0 ? "slab" : "wall", ExpectedUpperMm = expectedUpperMm,
            UpperY = top == null ? (double?)null : frame.Y(top.Point), UpperDimensions = upperDimensions };

        string GeometryKey(Reference reference)
        {
            string stable = reference.ConvertToStableRepresentation(doc);
            // Revit serializes the same measured rebar edge without LINEAR.
            return stable.EndsWith(":LINEAR", StringComparison.Ordinal) ? stable.Substring(0, stable.Length - 7) : stable;
        }
    }
}
