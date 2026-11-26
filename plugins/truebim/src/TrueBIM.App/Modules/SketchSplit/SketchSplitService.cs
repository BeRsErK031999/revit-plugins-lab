#if REVIT2022_OR_GREATER
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.RebarSplit.Revit;
using TrueBIM.App.Services;
using Point = System.Windows.Point;

namespace TrueBIM.App.Modules.SketchSplit;

public sealed class SketchSplitService
{
    private sealed class Failures : IFailuresPreprocessor
    {
        internal string? Reason { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var message in accessor.GetFailureMessages())
            {
                var id = message.GetFailureDefinitionId();
                if (message.GetSeverity() == FailureSeverity.Warning &&
                    (id == BuiltInFailures.OverlapFailures.FloorsOverlap || id == BuiltInFailures.OverlapFailures.DuplicateInstances || id == BuiltInFailures.PerformanceFailures.DuplicateInstances))
                    accessor.DeleteWarning(message);
            }
            var remaining = accessor.GetFailureMessages();
            if (remaining.Count == 0) return FailureProcessingResult.Continue;
            Reason = string.Join(" ", remaining.Select(m => m.GetDescriptionText()).Distinct());
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }


    public int CountParts(Element source)
    {
        var sketch = SketchIslands.GetSketch(source);
        return sketch == null ? 0 : SketchIslands.Read(sketch).Count;
    }

    public IReadOnlyList<ElementId> Split(Document doc, ElementId id, bool allowPinned, bool allowUngroup)
    {
        var source = doc.GetElement(id);
        if (source is not Floor && source is not Ceiling) throw new InvalidOperationException("Требуется перекрытие или потолок.");
        bool pinned = source.Pinned;
        if (pinned && !allowPinned) throw new InvalidOperationException("Элемент закреплён.");
        if (source.GroupId != ElementId.InvalidElementId && !allowUngroup) throw new InvalidOperationException($"Элемент в группе {source.GroupId}.");
        var sketch = SketchIslands.GetSketch(source) ?? throw new InvalidOperationException("Нет редактируемого эскиза.");
        var parts = SketchIslands.Read(sketch);
        if (parts.Count < 2) return [id];
        var originals = parts.SelectMany(p => p.Curves).ToDictionary(i => i, i => ((ModelCurve)doc.GetElement(i)).GeometryCurve.Clone());
        var affected = originals.Keys.Append(id).ToHashSet();
        var messages = new List<string>();
        var spots = new SketchSpotTransfer(doc, id, parts);
        var annotations = RebarSplitAnnotationSnapshot.Capture(doc, affected, messages, viewDimensionsOnly: true);
        if (messages.Count > 0) throw new InvalidOperationException(string.Join("\n", messages));
        double area = Measure(source, BuiltInParameter.HOST_AREA_COMPUTED);
        double volume = Measure(source, BuiltInParameter.HOST_VOLUME_COMPUTED);
        var samples = SampleFaces((HostObject)source);
        var hosted = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(instance => instance.Host?.Id == id).Select(instance => (instance.Id, Point: instance.Location is LocationPoint lp ? lp.Point : null)).ToArray();
        var railings = new FilteredElementCollector(doc).OfClass(typeof(Railing)).Cast<Railing>()
            .Where(r => r.HostId == id).Select(r => (r.Id, Path: r.GetPath().Select(c => c.Clone()).ToList())).ToArray();
        var created = new List<ElementId> { id };
        using var batch = new TransactionGroup(doc, "TrueBIM: разделить контуры");
        if (batch.Start() != TransactionStatus.Started) throw new InvalidOperationException("Не удалось начать операцию.");
        try
        {
            Change(doc, "Подготовить части", () =>
            {
                annotations.RemoveCaptured(doc);
                if (source.GroupId != ElementId.InvalidElementId)
                {
                    var group = (Group)doc.GetElement(source.GroupId);
                    if (group.GroupId != ElementId.InvalidElementId || group.Pinned)
                        throw new InvalidOperationException($"Вложенная или закреплённая группа {group.Id}. Разбиение пропущено.");
                    var members = group.GetMemberIds().ToArray();
                    if (group.GroupType.GetAvailableAttachedDetailGroupTypeIds().Count != 0)
                        throw new InvalidOperationException($"У группы {group.Id} есть присоединённое оформление.");
                    group.UngroupMembers();
                    if (members.Any(member => doc.GetElement(member) == null)) throw new InvalidOperationException("Разгруппирование удалило элементы.");
                }
                if (pinned) source.Pinned = false;
                for (int i = 1; i < parts.Count; i++)
                {
                    created.Add(spots.CopyHost(doc, source, i));
                }
            });
            var map = new RebarSplitElementMap { [id] = created };
            for (int i = 0; i < parts.Count; i++)
            {
                var target = doc.GetElement(created[i]);
                var targetSketch = SketchIslands.GetSketch(target) ?? throw new InvalidOperationException("Эскиз копии недоступен.");
                var targetParts = SketchIslands.Read(targetSketch);
                var keep = targetParts.Single(p => p.Shape.Bounds == parts[i].Shape.Bounds && Math.Abs(p.Area - parts[i].Area) < 1e-5);
                foreach (var original in parts[i].Curves)
                {
                    var corresponding = keep.Curves.Single(k => SketchIslands.SameCurve(((ModelCurve)doc.GetElement(k)).GeometryCurve, originals[original]));
                    map[original] = [corresponding];
                }
                using var edit = new SketchEditScope(doc, "Оставить область эскиза");
                edit.Start(targetSketch.Id);
                var remove = targetParts.Where(p => p != keep).SelectMany(p => p.Curves).ToArray();
                Change(doc, "Удалить контуры других участков", () => doc.Delete(remove));
                var sketchFailures = new Failures();
                edit.Commit(sketchFailures);
                if (sketchFailures.Reason != null) throw new InvalidOperationException(sketchFailures.Reason);
                var after = SketchIslands.Read(SketchIslands.GetSketch(target)!);
                if (after.Count != 1 || Math.Abs(after[0].Area - keep.Area) > 1e-4)
                    throw new InvalidOperationException("После изменения эскиза расходится площадь контура.");
            }
            map.LocateTagHost = (original, point) =>
            {
                if (original != id) return null;
                var indices = Enumerable.Range(0, parts.Count).Where(i => parts[i].Shape.FillContains(new Point(point.X, point.Y))).ToArray();
                return indices.Length == 1 ? created[indices[0]] : throw new InvalidOperationException("Точка марки расположена вне однозначного участка.");
            };
            Change(doc, "Восстановить оформление и закрепление", () =>
            {
                foreach (var railing in railings)
                {
                    if (doc.GetElement(railing.Id) is not Railing retained)
                        throw new InvalidOperationException($"Утрачено ограждение {railing.Id}.");
                    var points = railing.Path.SelectMany(c => c.Tessellate()).ToArray();
                    var indices = Enumerable.Range(0, parts.Count).Where(i => points.All(p => parts[i].Shape.FillContains(new Point(p.X, p.Y))
                        || parts[i].Shape.StrokeContains(new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 0.01), new Point(p.X, p.Y)))).ToArray();
                    if (indices.Length != 1) throw new InvalidOperationException($"Путь ограждения {railing.Id} выходит за границу участка.");
                    if (retained.HostId != created[indices[0]]) retained.HostId = created[indices[0]];
                    doc.Regenerate();
                    if (retained.HostId != created[indices[0]] || !retained.GetPath().Zip(railing.Path, SketchIslands.SameCurve).All(ok => ok)
                        || retained.GetPath().Count != railing.Path.Count)
                        throw new InvalidOperationException($"Не подтверждена геометрия ограждения {railing.Id}.");
                }
                annotations.Restore(doc, map);
                spots.Restore(doc, created, map);
                if (pinned) foreach (var targetId in created) doc.GetElement(targetId).Pinned = true;
                annotations.ReportLosses(doc, map);
                if (messages.Count > 0) throw new InvalidOperationException(string.Join("\n", messages));
            });
            var results = created.Select(doc.GetElement).Cast<HostObject>().ToArray();
            bool Changed(double a, double b) => Math.Abs(a - b) > Math.Max(1e-5, Math.Abs(a) * 1e-6);
            if (Changed(area, results.Sum(t => Measure(t, BuiltInParameter.HOST_AREA_COMPUTED))) ||
                Changed(volume, results.Sum(t => Measure(t, BuiltInParameter.HOST_VOLUME_COMPUTED))))
                throw new InvalidOperationException("После разбиения изменилась суммарная площадь или объём.");
            if (results.Any(t => t.Pinned != pinned || t.GetTypeId() != source.GetTypeId() || t.LevelId != source.LevelId))
                throw new InvalidOperationException("Не сохранены тип, уровень или закрепление.");
            foreach (var (original, point) in hosted)
            {
                if (doc.GetElement(original) is not FamilyInstance inst || point == null || inst.Location is not LocationPoint loc
                    || !loc.Point.IsAlmostEqualTo(point) || inst.Host is null || !created.Contains(inst.Host.Id)
                    || !parts[created.IndexOf(inst.Host!.Id)].Shape.FillContains(new Point(point.X, point.Y)))
                    throw new InvalidOperationException($"Не подтверждена основа или положение зависимого элемента {original}.");
            }
            CompareSurfaces(samples, results);
            if (batch.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Revit отменил операцию.");
            return created;
        }
        catch
        {
            if (batch.GetStatus() == TransactionStatus.Started) batch.RollBack();
            throw;
        }
    }

    private static void Change(Document doc, string name, Action action)
    {
        using var tx = new Transaction(doc, name);
        tx.Start();
        var options = tx.GetFailureHandlingOptions();
        var failures = new Failures();
        tx.SetFailureHandlingOptions(options.SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
        action();
        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException(failures.Reason ?? $"Revit отклонил действие «{name}».");
    }

    private static double Measure(Element e, BuiltInParameter parameter) => e.get_Parameter(parameter)?.AsDouble() ?? 0;

    private static List<(bool Top, XYZ Point)> SampleFaces(HostObject host)
    {
        var samples = new List<(bool, XYZ)>();
        foreach (bool top in new[] { true, false })
            foreach (var rf in top ? HostObjectUtils.GetTopFaces(host) : HostObjectUtils.GetBottomFaces(host))
            {
                var face = (Face)host.GetGeometryObjectFromReference(rf);
                var mesh = face.Triangulate();
                if (mesh == null) throw new InvalidOperationException("Revit не вернул сетку для проверки уклона.");
                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    var t = mesh.get_Triangle(i);
                    samples.Add((top, (t.get_Vertex(0) + t.get_Vertex(1) + t.get_Vertex(2)) / 3));
                }
            }
        return samples;
    }
    private static void CompareSurfaces(IEnumerable<(bool Top, XYZ Point)> samples, HostObject[] result)
    {
        foreach (bool top in new[] { true, false })
        {
            var faces = result.SelectMany(e => (top ? HostObjectUtils.GetTopFaces(e) : HostObjectUtils.GetBottomFaces(e))
                .Select(r => (Face)e.GetGeometryObjectFromReference(r))).ToArray();
            foreach (var sample in samples.Where(s => s.Top == top))
                if (!faces.Any(face => face.Project(sample.Point) is { Distance: < 0.0005 }))
                    throw new InvalidOperationException("Не подтверждено положение поверхности или уклон после разбиения.");
        }
    }
}
#endif
