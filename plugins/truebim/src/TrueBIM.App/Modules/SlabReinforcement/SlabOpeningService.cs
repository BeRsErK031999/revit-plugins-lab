#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed record OpeningLayer(double Diameter, double Depth);
internal sealed record OpeningSettings(double A, double B, double Cover, double Rs, double Rbt, OpeningLayer[] Layers);
internal sealed record OpeningResult(int Groups, int Straight, int Bends, int Replaced, List<string> Warnings);

internal static class SlabOpeningService
{
    internal static List<OpeningLayer>[] ReadLayers(Floor floor, SlabGeometry g)
    {
        var layers = Enumerable.Range(0, 4).Select(_ => new List<OpeningLayer>()).ToArray();
        var doc = floor.Document;
        void Add(RebarBarType type, Line line)
        {
            int direction = Math.Abs(line.Direction.DotProduct(g.X)) > .99999 ? 0 :
                Math.Abs(line.Direction.DotProduct(g.Y)) > .99999 ? 1 : -1;
            if (direction < 0) return;
            double depth = -g.Coord(line.GetEndPoint(0), g.N);
            bool lower = depth > g.Thickness / 2;
            var value = new OpeningLayer(Math.Round(type.BarNominalDiameter / Mm, 3),
                Math.Round((lower ? g.Thickness - depth : depth) / Mm, 2));
            var slot = layers[direction + (lower ? 2 : 0)];
            if (!slot.Contains(value)) slot.Add(value);
        }
        var host = RebarHostData.GetRebarHostData(floor);
        var ownedAreas = new HashSet<ElementId>(SlabReinforcementService.Owned(floor).OfType<AreaReinforcement>().Select(e => e.Id));
        foreach (var area in host.GetAreaReinforcementsInHost())
        foreach (var id in area.GetRebarInSystemIds())
        {
            var bars = (RebarInSystem)doc.GetElement(id);
            var type = (RebarBarType)doc.GetElement(bars.GetTypeId());
            if (!ownedAreas.Contains(area.Id) && !SlabOptions.IsBackground(type)) continue;
            for (int i = 0; i < bars.NumberOfBarPositions; i++)
            {
                if (!bars.DoesBarExistAtPosition(i)) continue;
                var lines = bars.GetTransformedCenterlineCurves(false, true, true, i);
                if (lines.Count == 1 && lines[0] is Line line) Add(type, line);
                break;
            }
        }
        var outlines = new HashSet<ElementId>(SlabReinforcementService.Tagged(floor, SlabReinforcementService.OpeningSchemaId).Select(e => e.Id));
        foreach (var bar in host.GetRebarsInHost().Where(r => !outlines.Contains(r.Id)))
        {
            var type = (RebarBarType)doc.GetElement(bar.GetTypeId());
            if (!SlabOptions.IsBackground(type) || !(doc.GetElement(bar.GetShapeId()).Name is "000" or "(Форма) 000")) continue;
            var lines = bar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
            if (lines.Count == 1 && lines[0] is Line line) Add(type, line);
        }
        return layers;
    }

    private static double Distance(CurveLoop a, CurveLoop b)
    {
        double best = double.MaxValue;
        foreach (var first in a)
            foreach (var second in b)
            {
                for (int i = 0; i < 2; i++)
                    best = Math.Min(best, Math.Min(first.Distance(second.GetEndPoint(i)), second.Distance(first.GetEndPoint(i))));
                if (best / Mm < 149.999) return best / Mm;
                if (first is Line && second is Line) continue; // Disjoint planar line segments reach their minimum at an endpoint.
                try
                {
                    first.ComputeClosestPoints(second, true, true, false, out var closest);
                    foreach (var point in closest) best = Math.Min(best, point.Distance);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // Parallel/concentric curves have non-unique minima; their bounded endpoints were tested above.
                }
            }
        return best / Mm;
    }

    private sealed record Detail(RebarBarType Type, bool Bend, XYZ Normal, XYZ[] Points);

    internal static OpeningResult Apply(Floor floor, SlabGeometry g, OpeningSettings o)
    {
        var doc = floor.Document;
        if (o.A <= 0 || o.B <= 0 || o.Cover <= 0 || o.Layers.Length != 4)
            throw new InvalidOperationException("Проверьте размеры обрамления и данные четырёх слоёв.");
        var contours = g.Loops.OrderByDescending(l => GeometryCreationUtilities.CreateExtrusionGeometry(new[] { l }, g.N, 1).Volume).ToList();
        var holes = contours.Skip(1).ToList();
        var boxes = holes.Select(l => {
            var x = g.Range(g.X, l); var y = g.Range(g.Y, l);
            return new OpeningBox(x.Min / Mm, x.Max / Mm, y.Min / Mm, y.Max / Mm);
        }).ToList();
        var groups = OpeningLayout.Groups(boxes, (i, j) => Distance(holes[i], holes[j]));
        var old = SlabReinforcementService.Tagged(floor, SlabReinforcementService.OpeningSchemaId);
        var warnings = new List<string>();
        var details = new List<Detail>();
        var types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
        var solids = new Dictionary<double, Solid>();
        var outside = new Dictionary<double, CurveLoop>();
        RebarBarType TypeFor(double diameter, bool bend)
        {
            var matches = types.Where(t => Math.Abs(t.BarNominalDiameter / Mm - diameter) < .05 &&
                (bend ? OpeningBarTypes.IsBend(t.Name) : OpeningBarTypes.IsStraight(t.Name))).ToList();
            string name = bend ? "(ПП.Обр) без п.м." : "(ПП.Фон) с п.м.";
            if (matches.Count != 1) throw new InvalidOperationException($"Для Ø{diameter:g} мм требуется один тип {name}. Найдено: {matches.Count}.");
            return matches[0];
        }
        if (groups.Count > 0)
        {
            var diameters = o.Layers.Select(l => TypeFor(l.Diameter, false).BarModelDiameter / Mm).ToArray();
            if (o.A < o.Cover + diameters.Max() / 2 - .05 || o.B < diameters.Max() - .05)
                throw new InvalidOperationException("Увеличьте a для соблюдения защитного слоя или b для размещения двух отдельных стержней.");
            foreach (int i in new[] { 0, 2 })
                if (Math.Abs(o.Layers[i].Depth - o.Layers[i + 1].Depth) + .05 < (diameters[i] + diameters[i + 1]) / 2)
                    throw new InvalidOperationException("Отметки пересекающихся направлений обрамления приводят к наложению стержней.");
        }
        for (int dir = 0; dir < 2 && groups.Count > 0; dir++)
        {
            XYZ axis = dir == 0 ? g.X : g.Y, cross = dir == 0 ? g.Y : g.X;
            var layers = new[] { o.Layers[dir], o.Layers[dir + 2] };
            var bars = layers.Select(l => TypeFor(l.Diameter, false)).ToArray();
            var anchors = layers.Select(l => SlabLayoutRules.OpeningAnchor(l.Diameter, o.Rs, o.Rbt)).ToArray();
            var depths = new[] { layers[0].Depth * Mm, g.Thickness - layers[1].Depth * Mm };
            for (int i = 0; i < 2; i++)
                if (layers[i].Depth * Mm < bars[i].BarModelDiameter / 2 || depths[1] <= depths[0])
                    throw new InvalidOperationException("Проверьте отметки осей обрамления по толщине плиты.");
            var rows = OpeningLayout.Rows(groups, dir == 1, o.A, o.B, anchors.Max());
            foreach (var row in rows)
            {
                XYZ origin = g.Origin + cross * (row.Station * Mm);
                XYZ Point(double along, int layer) => origin + axis * (along * Mm) - g.N * depths[layer];
                var range = g.Range(axis);
                double radius = bars.Max(t => t.BarModelDiameter) / 2;
                if (!solids.TryGetValue(radius, out var solid))
                {
                    solids.Add(radius, solid = SlabBands.CoveredSolid(g, o.Cover * Mm + radius));
                    outside.Add(radius, SlabBands.Offset(contours[0], g.N, o.Cover * Mm + radius, true));
                }
                using var test = Line.CreateBound(origin + g.N * .5 + axis * (range.Min - 1), origin + g.N * .5 + axis * (range.Max + 1));
                using var settings = new SolidCurveIntersectionOptions();
                using var sections = solid.IntersectWithCurve(test, settings);
                double lo = 0, hi = 0;
                for (int i = 0; i < sections.SegmentCount; i++)
                {
                    using var segment = sections.GetCurveSegment(i);
                    double p = g.Coord(segment.GetEndPoint(0), axis) / Mm, q = g.Coord(segment.GetEndPoint(1), axis) / Mm;
                    if (Math.Min(p,q) <= row.Start + .05 && Math.Max(p,q) >= row.End - .05)
                    { lo = Math.Min(p,q); hi = Math.Max(p,q); break; }
                }
                double lan = anchors.Max();
                bool left = lo > row.Start - lan + .05, right = hi < row.End + lan - .05;
                string location = $"{(dir == 0 ? "Буквы" : "Цифры")}, ряд {row.Station:F0} мм, участок {row.Start:F0}…{row.End:F0} мм";
                if (hi <= lo || (left && right))
                {
                    warnings.Add(location + (hi <= lo
                        ? ": ряд пересекает контур с учётом защитного слоя."
                        : ": для полной анкеровки недостаточно места с обеих сторон."));
                    continue;
                }
                if (!left && !right)
                {
                    for (int i = 0; i < 2; i++)
                        details.Add(new(bars[i], false, g.N, new[] { Point(row.Start - anchors[i], i), Point(row.End + anchors[i], i) }));
                    continue;
                }
                double web = left ? lo : hi;
                XYZ webPlan = origin + axis * (web * Mm);
                if (outside[radius].Any(c => c.Distance(webPlan) <= Tolerance))
                {
                    warnings.Add(location + ": места для анкеровки у наружного края плиты недостаточно.");
                    continue;
                }
                if (Math.Abs(layers[0].Diameter - layers[1].Diameter) > .05)
                {
                    warnings.Add(location + ": верх и низ имеют разные диаметры, для общей формы 200 требуется решение конструктора.");
                    continue;
                }
                var type = TypeFor(layers[0].Diameter, true);
                if (Math.Abs(type.BarModelDiameter / 2 - radius) > Tolerance)
                    throw new InvalidOperationException("У выбранных типов форм 000 и 200 разные физические диаметры. Проверьте защитный слой.");
                double tip = left ? row.End + lan : row.Start - lan;
                var points = new[] { Point(tip, 0), Point(web, 0), Point(web, 1), Point(tip, 1) };
                details.Add(new(type, true, axis.CrossProduct(g.N), points));
            }
        }
        if (old.Any(e => e.Pinned || e.GroupId != ElementId.InvalidElementId))
            throw new InvalidOperationException("Существующее обрамление закреплено или входит в группу.");
        if (details.Count == 0 && (old.Count == 0 || warnings.Count > 0)) return new(groups.Count, 0, 0, 0, warnings);
        using var group = new TransactionGroup(doc, "Обрамление отверстий плиты");
        group.Start();
        var checks = new List<(SlabEndCheck Check, RebarShape Shape)>();
        var failures = new SlabReinforcementService.SlabFailures();
        void Commit(Transaction tx)
        {
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit отменил обрамление. " + failures.Message);
        }
        using (var tx = new Transaction(doc, "Стержни обрамления отверстий"))
        {
            tx.Start();
            if (old.Count > 0) doc.Delete(old.Select(e => e.Id).ToList());
            var schema = SlabReinforcementService.OwnerSchema(SlabReinforcementService.OpeningSchemaId);
            var shapes = details.Select(d => d.Bend).Distinct().ToDictionary(b => b, b => SlabOptions.Shape(doc, b ? "200" : "000"));
            foreach (var detail in details)
            {
                var shape = shapes[detail.Bend];
                var curves = detail.Points.Zip(detail.Points.Skip(1), (a,b) => (Curve)Line.CreateBound(a,b)).ToList();
                var bar = Rebar.CreateFromCurvesAndShape(doc, shape, detail.Type, null, null, floor, detail.Normal, curves, RebarHookOrientation.Left, RebarHookOrientation.Left);
                if (bar == null) throw new InvalidOperationException($"Revit не принял обрамляющий стержень {shape.Name}.");
                SlabReinforcementService.Tag(bar, schema, floor);
                checks.Add((new(bar, new List<XYZ[]> { detail.Points }), shape));
            }
            Commit(tx);
        }
        var shifted = checks.Where(c => !c.Check.Matches()).ToList();
        if (shifted.Count > 0)
        {
            using var tx = new Transaction(doc, "Размеры и отметки обрамления");
            tx.Start();
            foreach (var item in shifted) item.Check.RestoreDimensions(item.Shape);
            doc.Regenerate();
            foreach (var item in shifted) item.Check.RestorePosition();
            Commit(tx);
            var wrong = checks.FirstOrDefault(c => !c.Check.Matches()).Check;
            if (wrong != null) throw new InvalidOperationException($"Привязки Revit изменили обрамляющий стержень {wrong.Bar.Id}. Операция отменена.");
        }
        group.Assimilate();
        return new(groups.Count, details.Count(d => !d.Bend), details.Count(d => d.Bend), old.Count, warnings);
    }
}
#endif
