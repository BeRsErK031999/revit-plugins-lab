#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed record SlabResult(int Areas, int EndSets, int EndBars, int Frames, int Deleted, List<string> Warnings, HashSet<ElementId> ReviewBars);
internal static class SlabReinforcementService
{
    private static readonly Guid SchemaId = new("9C101632-7FAB-4413-A6F6-3493E017D17A");
    private const string HostField = "SlabUniqueId";
    private static Schema OwnerSchema()
    {
        var existing = Schema.Lookup(SchemaId);
        if (existing != null)
            return existing;
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("TrueBIMSlabReinforcement");
        builder.AddSimpleField(HostField, typeof(string));
        return builder.Finish();
    }

    private static void Tag(Element element, Schema schema, Floor host)
    {
        var entity = new Entity(schema);
        entity.Set(schema.GetField(HostField), host.UniqueId);
        element.SetEntity(entity);
    }

    internal static List<Element> Owned(Floor floor)
    {
        var schema = Schema.Lookup(SchemaId);
        if (schema == null)
            return new();
        return new FilteredElementCollector(floor.Document).WherePasses(new ExtensibleStorageFilter(SchemaId)).Where(e => e.GetEntity(schema).Get<string>(schema.GetField(HostField)) == floor.UniqueId).ToList();
    }

    internal static List<Element> Manual(Floor floor, HashSet<ElementId> owned)
    {
        var d = floor.Document;
        var host = RebarHostData.GetRebarHostData(floor);
        var list = host.GetRebarsInHost().Where(r => !owned.Contains(r.Id) && SlabOptions.IsBackground(d.GetElement(r.GetTypeId()))).Cast<Element>().ToList();
        foreach (var area in host.GetAreaReinforcementsInHost().Where(a => !owned.Contains(a.Id)))
        {
            var children = area.GetRebarInSystemIds();
            if (children.Count > 0 && children.All(id => SlabOptions.IsBackground(d.GetElement(d.GetElement(id).GetTypeId()))))
                list.Add(area);
        }

        return list;
    }

    internal static SlabResult Apply(Floor floor, XYZ direction, Line? firstEdge, Line? secondEdge, SlabOptions options)
    {
        var d = floor.Document;
        var geometry = new SlabGeometry(floor, direction);
        options.Validate(geometry);
        var straight = SlabOptions.Shape(d, "000");
        var bend = SlabOptions.Shape(d, "200");
        var owned = Owned(floor);
        var frames = owned.OfType<FamilyInstance>().ToList();
        bool keepFrames = frames.Count > 0 && !options.ReplaceFrames;
        var remove = owned.Where(e => e is not FamilyInstance || !keepFrames).ToList();
        if (options.ReplaceManual)
            remove.AddRange(Manual(floor, new HashSet<ElementId>(owned.Select(e => e.Id))));
        if (remove.Any(e => e.Pinned || e.GroupId != ElementId.InvalidElementId))
            throw new InvalidOperationException("Часть заменяемых элементов закреплена или входит в группу. Проверьте выделенную плиту.");
        var warnings = new List<string>();
        var review = new HashSet<ElementId>();
        var checks = new List<SlabEndCheck>();
        using var group = new TransactionGroup(d, "Армирование выбранной плиты");
        group.Start();
        using var tx = new Transaction(d, "КР · Армирование плиты");
        tx.Start();
        var failure = new SlabFailures();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failure).SetClearAfterRollback(true));
        try
        {
            if (remove.Count > 0)
                d.Delete(remove.Select(e => e.Id).ToList());
            var schema = OwnerSchema();
            int areaCount = 0, sets = 0, count = 0;
            for (int i = 0; i < 2; i++)
            {
                var area = new SlabBackground(floor, geometry, options, i).Create();
                foreach (var part in area)
                    Tag(part, schema, floor);
                areaCount += area.Count;
                var ends = SlabEnds.Create(floor, geometry, options, area, i, straight, bend, warnings, review, checks);
                foreach (var bar in ends)
                    Tag(bar, schema, floor);
                sets += ends.Count;
                count += ends.Sum(r => r.Quantity);
            }

            int frameCount = keepFrames ? frames.Count : 0;
            if (options.PlaceFrames && !keepFrames)
            {
                if (firstEdge == null || secondEdge == null)
                    throw new InvalidOperationException("Для начала сетки каркасов выберите две грани.");
                var created = SlabFrames.Place(floor, geometry, options, firstEdge, secondEdge, warnings);
                foreach (var frame in created)
                    Tag(frame, schema, floor);
                frameCount = created.Count;
            }

            if (tx.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit отменил изменения. " + failure.Message);
            // Revit can snap even explicitly supplied U tips to a neighbouring host face on commit.
            // Check physical instances after constraints have been solved, including every member of a set.
            var snapped = checks.Where(check => !check.Matches()).ToList();
            if (snapped.Count > 0)
            {
                using var correction = new Transaction(d, "Размеры и касание торцевых стержней");
                correction.Start();
                foreach (var check in snapped) check.RestoreDimensions(bend);
                d.Regenerate();
                foreach (var check in snapped) check.RestorePosition();
                correction.SetFailureHandlingOptions(correction.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(failure).SetClearAfterRollback(true));
                if (correction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit не принял размеры торцевых деталей. " + failure.Message);
                var wrong = checks.FirstOrDefault(check => !check.Matches());
                if (wrong != null)
                    throw new InvalidOperationException($"Привязки Revit изменили геометрию торцевого стержня {wrong.Bar.Id}. Операция отменена.");
            }
            group.Assimilate();
            return new(areaCount, sets, count, frameCount, remove.Count, warnings, review);
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started)
                tx.RollBack();
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            throw;
        }
    }

    private sealed class SlabFailures : IFailuresPreprocessor
    {
        internal string Message { get; private set; } = "";

        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            var errors = a.GetFailureMessages().Where(m => m.GetSeverity() >= FailureSeverity.Error).ToList();
            if (errors.Count == 0)
                return FailureProcessingResult.Continue;
            Message = string.Join(" ", errors.Select(e => e.GetDescriptionText()).Distinct());
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
#endif
