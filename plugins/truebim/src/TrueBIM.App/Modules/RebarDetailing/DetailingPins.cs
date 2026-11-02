using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    private static void PlacePins(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry,
        DetailingOptions options, DetailingResult result)
    {
        const string family = "(Узел) Арматура • Ш1";
        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
        var symbol = symbols.FirstOrDefault(s => s.FamilyName == family && s.Name.Replace('А', 'A') == "A240");
        if (symbol == null) { result.Notes.Add("Для шпилек загрузите семейство «" + family + "», тип «A240»."); return; }
        var existing = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Symbol.FamilyName == family && f.Location is LocationPoint).ToList();
        if (!symbol.IsActive) symbol.Activate();
        foreach (var section in Sections(frame, geometry, options))
        {
            if (section.Faces.Count(f => f.Horizontal) < 2) continue;
            var vertical = section.Bars.Where(b => !IsPin(b) && b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999
                && s.Start.DistanceTo(s.End) > 500 / 304.8)).ToList();
            var transverse = section.Bars.Where(b => !IsPin(b) && !vertical.Contains(b)).ToList();
            if (vertical.Count == 0 || transverse.Count == 0) continue;
            var rows = DetailingBarGroups.Unique(transverse.SelectMany(b => b.Positions).Select(frame.Y)
                .Where(y => y >= section.Wall.Bottom - 0.1 / 304.8 && y <= section.Wall.Top + 0.1 / 304.8), 0.1 / 304.8);
            var host = geometry.Hosts.FirstOrDefault(h => h.Key == section.HostKey);
            double thickness = host?.Element is Wall wall ? wall.Width : section.Wall.Width;
            double length = DetailingSectionLayout.PinLengthMm(thickness * 304.8) / 304.8;
            if (length <= 0) { result.Notes.Add("Шпильки: толщина конструкции должна превышать 50 мм."); continue; }
            var placed = new List<XYZ>();
            foreach (double y in rows.Where((_, index) => index % 2 == 0))
            {
                XYZ center = frame.Point(section.Wall.CenterX, y);
                if (existing.Any(f => frame.Flatten(((LocationPoint)f.Location).Point).DistanceTo(center) < 1 / 304.8))
                { placed.Add(center); continue; }
                FamilyInstance pin = doc.Create.NewFamilyInstance(center, symbol, view);
                SetLength(pin, "A", length);
                doc.Regenerate();
                XYZ axis = pin.GetTransform().BasisX.Normalize();
                double angle = Math.Atan2(frame.Normal.DotProduct(axis.CrossProduct(frame.Right)), axis.DotProduct(frame.Right));
                if (Math.Abs(angle) > 1e-8) ElementTransformUtils.RotateElement(doc, pin.Id, Line.CreateBound(center, center + frame.Normal), angle);
                doc.Regenerate();
                // Parametric detail families can offset their insertion origin
                // when A changes. Center the actual location on the wall axis.
                if (pin.Location is LocationPoint location)
                    ElementTransformUtils.MoveElement(doc, pin.Id, center - frame.Flatten(location.Point));
                MarkDetail(pin, "pin"); result.Pins++; result.Details++;
                existing.Add(pin); placed.Add(center);
            }
            if (placed.Count == 0) continue;
            var diameters = vertical.Select(b => b.Element.Document.GetElement(b.Element.GetTypeId())
                ?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER)?.AsDouble() ?? 0)
                .Where(d => d > 0).Select(d => Math.Round(d * 304.8, 1)).Distinct().OrderBy(d => d).ToList();
            if (diameters.Count == 0) { result.Notes.Add("Шпильки: не определён диаметр вертикальной арматуры для выноски."); continue; }
            string top = "t" + Math.Round(thickness * 304.8).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "_d" + string.Join("/", diameters.Select(d => d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
            XYZ target = placed[placed.Count / 2];
            double noteY = frame.Y(target) - frame.Paper(5);
            var manual = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().ToList();
            var obstacles = manual.Select(e => LabelObstacle(e, view, frame)).Where(b => b.HasValue).Select(b => b!.Value).ToList();
            double noteX = section.BreakRight + frame.Paper(25);
            for (int attempt = 0; attempt < 20 && obstacles.Any(b => b.Right >= noteX - frame.Paper(15)
                && b.Left <= noteX + frame.Paper(15) && b.Bottom <= noteY + frame.Paper(4) && b.Top >= noteY - frame.Paper(4)); attempt++)
                noteY -= frame.Paper(8);
            AddDetailNote(doc, view, frame, symbols, manual, result, top, "в шахм. порядке", frame.Point(noteX, noteY), target, "pin-note");
        }
    }
}
