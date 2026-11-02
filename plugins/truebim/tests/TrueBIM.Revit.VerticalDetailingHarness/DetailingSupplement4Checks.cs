using Autodesk.Revit.DB;
using TrueBIM.App.Modules.RebarDetailing;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication
{
    private object MeasureSupplement4(Document doc, View view, DetailingOptions options)
    {
        var frame = new DetailingFrame(view);
        var geometry = DetailingGeometry.Read(doc, view);
        var sections = (List<DetailingSection>)typeof(RebarDetailingService)
            .GetMethod("Sections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { frame, geometry, options })!;
        var owned = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().ToList();
        var pins = owned.OfType<FamilyInstance>().Where(f => f.Symbol.FamilyName == "(Узел) Арматура • Ш1"
            && RebarDetailingService.OwnedDetail(f)).ToList();
        var pinChecks = pins.Select(pin =>
        {
            XYZ point = ((LocationPoint)pin.Location).Point;
            var section = sections.OrderBy(s => Math.Abs(s.Wall.CenterX - frame.X(point))).First();
            var host = geometry.Hosts.FirstOrDefault(h => h.Key == section.HostKey);
            double thickness = host?.Element is Wall wall ? wall.Width : section.Wall.Width;
            double lengthMm = pin.LookupParameter("A").AsDouble() * 304.8;
            double centerErrorMm = Math.Abs(section.Wall.CenterX - frame.X(point)) * 304.8;
            return new { Id = pin.Id.Value, LengthMm = lengthMm, ExpectedLengthMm = thickness * 304.8 - 50, CenterErrorMm = centerErrorMm,
                X = frame.X(point), Y = frame.Y(point), Correct = Math.Abs(lengthMm - (thickness * 304.8 - 50)) < 0.1 && centerErrorMm < 0.1 };
        }).ToList();
        var rowChecks = sections.Where(s => s.Faces.Count(f => f.Horizontal) >= 2).Select(section =>
        {
            bool Vertical(DetailingBar b) => b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999 && s.Start.DistanceTo(s.End) > 500 / 304.8);
            var rows = section.Bars.Where(b => !Vertical(b)).SelectMany(b => b.Positions).Select(frame.Y)
                .Where(y => y >= section.Wall.Bottom - 0.1 / 304.8 && y <= section.Wall.Top + 0.1 / 304.8)
                .Select(y => Math.Round(y * 304.8, 1)).Distinct().OrderBy(y => y).ToList();
            var expected = rows.Where((_, index) => index % 2 == 0).ToList();
            var actual = pinChecks.Where(p => Math.Abs(p.X - section.Wall.CenterX) < 0.1 / 304.8)
                .Select(p => Math.Round(p.Y * 304.8, 1)).OrderBy(y => y).ToList();
            bool applicable = section.Bars.Any(Vertical) && rows.Count > 0;
            return new { section.HostKey, Expected = expected, Actual = actual, Applicable = applicable, Correct = !applicable || expected.SequenceEqual(actual) };
        }).ToList();
        var dimensions = owned.OfType<Dimension>().Where(d => d is not SpotDimension).ToList();
        var axisPoints = geometry.Bars.SelectMany(b => b.DimensionReferences).ToList();
        var leftReferences = dimensions.Where(d => d.Curve is Line line && Math.Abs(line.Direction.DotProduct(frame.Up)) > 0.999)
            .SelectMany(d => d.References.Cast<Reference>()).Select(r => r.ConvertToStableRepresentation(doc)).Distinct()
            .Select(stable => axisPoints.FirstOrDefault(a => a.Candidates.Any(r => r.ConvertToStableRepresentation(doc) == stable)))
            .Where(a => a != null).Select(a =>
            {
                double y = frame.Y(a!.Point), x = frame.X(a.Point);
                double left = axisPoints.Where(other => Math.Abs(frame.Y(other.Point) - y) < 0.1 / 304.8
                    && Math.Abs(other.Direction.DotProduct(frame.Up)) < 0.001).Select(other => frame.X(other.Point)).DefaultIfEmpty(x).Min();
                return new { X = x, Y = y, Left = left, Correct = x - left < 0.1 / 304.8 };
            }).ToList();
        var spots = owned.OfType<SpotDimension>().Select(s => s.get_Parameter(BuiltInParameter.SPOT_ELEV_SINGLE_OR_UPPER_VALUE)?.AsDouble()).ToList();
        bool uniqueSpots = spots.All(v => v.HasValue) && spots.Count == spots.Select(v => Math.Round(v!.Value * 304.8, 1)).Distinct().Count();
        bool noTopDimensions = !dimensions.Any(d => d.Curve is Line line && Math.Abs(line.Direction.DotProduct(frame.Right)) > 0.999
            && frame.Y(line.Origin) > sections.Max(s => s.Wall.Top) + 1 / 304.8);
        var notes = owned.OfType<FamilyInstance>().Where(f => f is AnnotationSymbol)
            .Select(f => new { Top = f.LookupParameter("Верхняя строка")?.AsString(), Bottom = f.LookupParameter("Нижняя строка")?.AsString() }).ToList();
        bool pinNote = pins.Count == 0 || notes.Any(n => n.Top?.StartsWith("t", StringComparison.Ordinal) == true && n.Bottom == "в шахм. порядке");
        return new { Passed = pinChecks.All(p => p.Correct) && rowChecks.All(r => r.Correct) && leftReferences.All(r => r.Correct)
                && uniqueSpots && noTopDimensions && pinNote,
            PinChecks = pinChecks, RowChecks = rowChecks, LeftReferences = leftReferences, SpotValues = spots, UniqueSpots = uniqueSpots,
            NoTopDimensions = noTopDimensions, PinNote = pinNote, Notes = notes };
    }
}
