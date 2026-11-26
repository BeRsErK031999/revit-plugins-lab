#if REVIT2022_OR_GREATER
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.RebarSplit.Revit;
using Point = System.Windows.Point;

namespace TrueBIM.App.Modules.SketchSplit;

/// <summary>Copies spot slopes with their host. Revit has no public constructor for a spot slope.</summary>
internal sealed class SketchSpotTransfer
{
    private sealed record Mark(ElementId Id, ElementId View, ElementId Type, XYZ Origin, bool Pinned, int Part);
    private readonly List<Mark> marks = new();
    private readonly Dictionary<ElementId, ElementId> replacements = new();
    private readonly Dictionary<ElementId, OverrideGraphicSettings> overrides = new();

    public SketchSpotTransfer(Document doc, ElementId host, List<SketchIsland> parts)
    {
        foreach (var spot in new FilteredElementCollector(doc).OfClass(typeof(SpotDimension)).Cast<SpotDimension>())
        {
            if (!spot.References.Cast<Reference>().Any(r => r.ElementId == host)) continue;
            var p = spot.Origin;
            var indexes = Enumerable.Range(0, parts.Count).Where(i => parts[i].Shape.FillContains(new Point(p.X, p.Y))).ToArray();
            if (indexes.Length != 1)
            {
                var reference = spot.References.Cast<Reference>().First(r => r.ElementId == host);
                var face = doc.GetElement(host).GetGeometryObjectFromReference(reference) as Face;
                var anchor = face?.Project(p)?.XYZPoint;
                if (anchor != null)
                    indexes = Enumerable.Range(0, parts.Count).Where(i => parts[i].Shape.FillContains(new Point(anchor.X, anchor.Y))).ToArray();
                if (indexes.Length != 1 && face?.Triangulate() is Mesh mesh)
                {
                    var supported = new HashSet<int>();
                    for (int k = 0; k < mesh.NumTriangles; k++)
                    {
                        var triangle = mesh.get_Triangle(k);
                        var point = (triangle.get_Vertex(0) + triangle.get_Vertex(1) + triangle.get_Vertex(2)) / 3;
                        for (int i = 0; i < parts.Count; i++)
                            if (parts[i].Shape.FillContains(new Point(point.X, point.Y))) supported.Add(i);
                    }
                    indexes = supported.ToArray();
                }
                if (indexes.Length != 1) throw new InvalidOperationException($"Для отметки {spot.Id} не определён участок эскиза.");
            }
            marks.Add(new(spot.Id, spot.OwnerViewId, spot.GetTypeId(), p, spot.Pinned, indexes[0]));
            overrides[spot.Id] = ((View)doc.GetElement(spot.OwnerViewId)).GetElementOverrides(spot.Id);
        }
        // Keep the original where relinking marks across the most views would be necessary.
        var preferred = marks.GroupBy(m => m.Part).OrderByDescending(g => g.Select(m => m.View).Distinct().Count())
            .ThenByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.Key ?? 0;
        if (preferred != 0)
        {
            (parts[0], parts[preferred]) = (parts[preferred], parts[0]);
            for (int i = 0; i < marks.Count; i++)
            {
                var mark = marks[i];
                marks[i] = mark with { Part = mark.Part == preferred ? 0 : mark.Part == 0 ? preferred : mark.Part };
            }
        }
    }

    public ElementId CopyHost(Document doc, Element source, int part)
    {
        var move = marks.Where(m => m.Part == part).ToList();
        if (move.Count == 0) return ElementTransformUtils.CopyElement(doc, source.Id, XYZ.Zero)
            .Select(doc.GetElement).Single(e => e.GetType() == source.GetType()).Id;
        var primary = (View)doc.GetElement(move.GroupBy(m => m.View).OrderByDescending(g => g.Count()).First().Key);
        var temp = new List<ElementId>();
        var batch = new List<ElementId> { source.Id };
        foreach (var m in move)
        {
            if (m.View == primary.Id) { batch.Add(m.Id); continue; }
            var intermediate = CopySpot(doc, (View)doc.GetElement(m.View), primary, m.Id);
            AlignOnPlan(doc, intermediate, m);
            if (!SamePlanAndHost(intermediate, m, source.Id)) throw new InvalidOperationException($"Вид не поддерживает перенос отметки {m.Id}. Положение {m.Origin} → {intermediate.Origin}, тип {m.Type} → {intermediate.GetTypeId()}, ссылка {string.Join(",", intermediate.References.Cast<Reference>().Select(r => r.ElementId))}.");
            temp.Add(intermediate.Id);
            batch.Add(intermediate.Id);
        }
        var copied = ElementTransformUtils.CopyElements(primary, batch, primary, Transform.Identity, new CopyPasteOptions()).Select(doc.GetElement).ToList();
        doc.Regenerate();
        var host = copied.Single(e => e.GetType() == source.GetType());
        var available = copied.OfType<SpotDimension>().ToList();
        foreach (var m in move)
        {
            var duplicate = available.FirstOrDefault(d => SamePlanAndHost(d, m, host.Id))
                ?? throw new InvalidOperationException($"Revit не перепривязал копию отметки {m.Id} к новому участку.");
            available.Remove(duplicate);
            replacements.Add(m.Id, duplicate.Id);
        }
        if (available.Count != 0) throw new InvalidOperationException("Revit создал лишние отметки участка.");
        doc.Delete(temp.Concat(move.Select(m => m.Id)).ToArray());
        return host.Id;
    }

    public void Restore(Document doc, IReadOnlyList<ElementId> hosts, RebarSplitElementMap map)
    {
        foreach (var m in marks)
        {
            var target = (SpotDimension?)doc.GetElement(replacements.TryGetValue(m.Id, out var replacement) ? replacement : m.Id)
                ?? throw new InvalidOperationException($"Утрачена отметка {m.Id}.");
            var view = (View)doc.GetElement(m.View);
            if (target.OwnerViewId != m.View)
            {
                // At this point the old and new hosts occupy disjoint plan regions.
                var final = CopySpot(doc, (View)doc.GetElement(target.OwnerViewId), view, target.Id);
                AlignOnPlan(doc, final, m);
                doc.Delete(target.Id);
                target = final;
            }
            if (!Matches(target, m, hosts[m.Part])) throw new InvalidOperationException($"Привязка отметки {m.Id} к участку {hosts[m.Part]} не подтверждена. Получена ссылка {string.Join(",", target.References.Cast<Reference>().Select(r => r.ElementId))}, точка {target.Origin}, исходная {m.Origin}.");
            if (m.Part != 0)
            {
                view.SetElementOverrides(target.Id, overrides[m.Id]);
                if (target.Pinned != m.Pinned) target.Pinned = m.Pinned;
                map[m.Id] = [target.Id];
            }
        }
    }

    private static bool Matches(SpotDimension target, Mark m, ElementId host) =>
        target.GetTypeId() == m.Type && target.Origin.DistanceTo(m.Origin) < 0.0001 &&
        target.References.Cast<Reference>().Any(r => r.ElementId == host);

    private static bool SamePlanAndHost(SpotDimension target, Mark m, ElementId host) =>
        target.GetTypeId() == m.Type && Math.Abs(target.Origin.X - m.Origin.X) < 0.0001
        && Math.Abs(target.Origin.Y - m.Origin.Y) < 0.0001 && target.References.Cast<Reference>().Any(r => r.ElementId == host);

    private static void AlignOnPlan(Document doc, SpotDimension copy, Mark mark)
    {
        var delta = mark.Origin - copy.Origin;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 1e-6) return;
        if (copy.Pinned) copy.Pinned = false;
        ElementTransformUtils.MoveElement(doc, copy.Id, new XYZ(delta.X, delta.Y, 0));
        doc.Regenerate();
    }

    private static SpotDimension CopySpot(Document doc, View from, View to, ElementId id)
    {
        var ids = ElementTransformUtils.CopyElements(from, new[] { id }, to, Transform.Identity, new CopyPasteOptions());
        doc.Regenerate();
        return ids.Select(doc.GetElement).OfType<SpotDimension>().Single();
    }
}
#endif
