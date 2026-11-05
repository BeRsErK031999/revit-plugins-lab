using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace TrueBIM.App.Modules.GeometricFinishes;

public sealed class FinishService
{
    private readonly FinishCatalog catalog;
    public FinishService(FinishCatalog catalog) => this.catalog = catalog;

    public FinishResult Create(FinishSettings settings, Func<int, int, bool>? progress = null)
    {
        Document doc = catalog.Document;
        FinishPlan plan = catalog.BuildPlan(settings);
        FinishResult result = new();
        result.Issues.AddRange(plan.Issues);
        var assignments = settings.Assignments.GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.Last());
        HashSet<string> existing = FinishOwnership.Existing(doc);
        FinishSlabLocator? slabs = settings.Kind != FinishKind.Walls && settings.Elevation == FinishElevation.Slab ? new FinishSlabLocator(doc) : null;
        var grouped = plan.Targets.GroupBy(t => t.Room.Id).ToList();
        result.SkippedRooms = plan.RoomCount - grouped.Count;
        if (grouped.Count == 0) return result;
        if (settings.Kind == FinishKind.Walls)
        {
            // Capture cut geometry before joins or room-bounding finishes change any source wall.
            foreach (FinishTarget target in plan.Targets.Where(t => !existing.Contains(FinishOwnership.Identity(t, settings.Kind))))
            {
                try { target.Openings = FinishOpenings.Capture(target, FinishGeometry.RoomBase(target.Room) + settings.OffsetMm * FinishGeometry.FeetPerMm, FinishGeometry.RoomTop(target.Room)); }
                catch (Exception ex) { target.OpeningError = ex.Message; }
            }
        }
        using TransactionGroup batch = new(doc, Title(settings.Kind));
        batch.Start();
        int index = 0;
        try
        {
            foreach (var group in grouped)
            {
                if (progress != null && !progress(index++, grouped.Count))
                {
                    batch.RollBack();
                    result.CreatedIds.Clear(); result.CreatedRooms = 0; result.Cancelled = true;
                    return result;
                }
                Room room = group.First().Room;
                if (group.Any(t => !assignments.ContainsKey(t.Key)))
                {
                    result.SkippedRooms++;
                    result.Issues.Add(FinishCatalog.Issue(room, "Для части границ не задан вариант отделки. Обновите таблицу настроек."));
                    continue;
                }
                var targets = group.Where(t => assignments.TryGetValue(t.Key, out FinishAssignment? a) && a.Enabled).ToList();
                var newTargets = targets.Where(t => !existing.Contains(FinishOwnership.Identity(t, settings.Kind))).ToList();
                if (newTargets.Count != targets.Count) result.Issues.Add(FinishCatalog.Issue(room, "Ранее созданная этим плагином отделка пропущена. Обновление в этой версии не выполняется."));
                if (newTargets.Count == 0) { result.SkippedRooms++; continue; }
                var warnings = new List<string>();
                var wallJoins = new List<(Wall Finish, FinishSourceWall Source)>();
                using Transaction tx = new(doc, Title(settings.Kind) + " · " + room.Number);
                try
                {
                    // Resolve every type and centerline before any room-bounding finish can change the room.
                    var prepared = newTargets.Select(target =>
                    {
                        FinishAssignment assignment = assignments[target.Key];
                        ElementType? type = string.IsNullOrWhiteSpace(assignment.TypeUniqueId) ? null : doc.GetElement(assignment.TypeUniqueId) as ElementType;
                        if (type == null) throw new InvalidOperationException("Не выбран или удалён тип отделки для строки «" + target.Label + "».");
                        bool wall = settings.Kind == FinishKind.Walls;
                        if (wall && type is not WallType || !wall && settings.Kind == FinishKind.Ceilings && !settings.CeilingAsFloor && type is not CeilingType
                            || !wall && (settings.Kind == FinishKind.Floors || settings.CeilingAsFloor) && type is not FloorType)
                            throw new InvalidOperationException("Тип отделки относится к другой категории.");
                        Curve? center = wall ? FinishGeometry.WallCenterline(target, (WallType)type) : null;
                        return (Target: target, Assignment: assignment, Type: type, Center: center);
                    }).ToList();
                    tx.Start();
                    FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                    failureOptions.SetFailuresPreprocessor(new FinishFailures(warnings));
                    failureOptions.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(failureOptions);
                    var created = new List<ElementId>();
                    foreach (var item in prepared)
                    {
                        List<Element> elements = settings.Kind == FinishKind.Walls
                            ? CreateWall(doc, item.Target, (WallType)item.Type, item.Center!, settings, warnings)
                            : CreateHorizontal(doc, item.Target, (HostObjAttributes)item.Type, settings, slabs);
                        foreach (Element element in elements)
                        {
                            Parameter? bounding = element.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (bounding == null || bounding.IsReadOnly) throw new InvalidOperationException("Revit не позволяет задать признак «Граница помещения» у выбранного типа отделки.");
                            bounding.Set(item.Assignment.RoomBounding ? 1 : 0);
                            FinishParameters.Copy(room, element, settings);
                            FinishOwnership.Stamp(element, item.Target, settings.Kind);
                            created.Add(element.Id);
                            if (element is Wall finishWall && item.Target.Source != null) wallJoins.Add((finishWall, item.Target.Source));
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit отклонил геометрию; изменения помещения отменены.");
                    result.CreatedIds.AddRange(created);
                    if (created.Count > 0) result.CreatedRooms++;
                    else result.SkippedRooms++;
                    if (wallJoins.Count > 0) JoinCreatedWalls(doc, wallJoins, settings.JoinWalls, warnings);
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    result.SkippedRooms++;
                    result.Issues.Add(FinishCatalog.Issue(room, ex.Message));
                }
                foreach (string warning in warnings.Distinct()) result.Issues.Add(FinishCatalog.Issue(room, warning));
            }
            if (batch.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Не удалось завершить создание отделки.");
            progress?.Invoke(grouped.Count, grouped.Count);
            return result;
        }
        catch
        {
            if (batch.GetStatus() == TransactionStatus.Started) batch.RollBack();
            throw;
        }
    }

    private static List<Element> CreateWall(Document doc, FinishTarget target, WallType type, Curve center, FinishSettings settings, List<string> warnings)
    {
        Room room = target.Room;
        Level level = (Level)doc.GetElement(room.LevelId);
        double bottom = FinishGeometry.RoomBase(room) + settings.OffsetMm * FinishGeometry.FeetPerMm;
        double top = FinishGeometry.RoomTop(room);
        if (top <= bottom + doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Высота отделочной стены должна быть положительной.");
        Curve curve = center.CreateTransformed(Transform.CreateTranslation(XYZ.BasisZ * (level.Elevation - center.GetEndPoint(0).Z)));
        Wall wall = Wall.Create(doc, curve, type.Id, level.Id, top - bottom, bottom - level.Elevation, false, false);
        WallUtils.DisallowWallJoinAtEnd(wall, 0);
        WallUtils.DisallowWallJoinAtEnd(wall, 1);
        wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)?.Set(0);
        doc.Regenerate();
        FinishOpenings.Cut(doc, wall, target, bottom, top);
        return new List<Element> { wall };
    }

    private static void JoinCreatedWalls(Document doc, IReadOnlyList<(Wall Finish, FinishSourceWall Source)> walls, bool joinBase, List<string> warnings)
    {
        // Optional joins have their own transaction: a rejected join must not discard correct walls and openings.
        using Transaction join = new(doc, "Соединение отделочных стен");
        join.Start();
        var joinWarnings = new List<string>();
        join.SetFailureHandlingOptions(join.GetFailureHandlingOptions().SetFailuresPreprocessor(new FinishFailures(joinWarnings)).SetClearAfterRollback(true));
        try
        {
            for (int i = 0; i < walls.Count; i++)
            {
                if (joinBase && !walls[i].Source.Linked) TryJoin(doc, walls[i].Source.Wall, walls[i].Finish);
                // Resolve the small overlap at room corners without allowing automatic joins to unrelated walls.
                BoundingBoxXYZ? a = walls[i].Finish.get_BoundingBox(null);
                if (a == null) continue;
                for (int j = i + 1; j < walls.Count; j++)
                {
                    BoundingBoxXYZ? b = walls[j].Finish.get_BoundingBox(null);
                    if (b != null && a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y)
                        TryJoin(doc, walls[i].Finish, walls[j].Finish);
                }
            }
            if (join.Commit() != TransactionStatus.Committed)
                warnings.Add("Отделка и проёмы созданы. Соединение стен отменено: Revit не смог сохранить примыкания.");
        }
        catch (Exception ex)
        { if (join.GetStatus() == TransactionStatus.Started) join.RollBack(); warnings.Add("Отделка создана; соединение не выполнено: " + ex.Message); }
        warnings.AddRange(joinWarnings);
    }
    private static void TryJoin(Document doc, Element a, Element b)
    {
        using SubTransaction attempt = new(doc);
        attempt.Start();
        try { if (!JoinGeometryUtils.AreElementsJoined(doc, a, b)) JoinGeometryUtils.JoinGeometry(doc, a, b); attempt.Commit(); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { attempt.RollBack(); }
    }

    private static List<Element> CreateHorizontal(Document doc, FinishTarget target, HostObjAttributes type, FinishSettings settings, FinishSlabLocator? slabs)
    {
        Room room = target.Room;
        Level level = (Level)doc.GetElement(room.LevelId);
        bool ceiling = settings.Kind == FinishKind.Ceilings;
        double thickness = type.GetCompoundStructure()?.GetWidth() ?? 0;
        double elevation = settings.Elevation switch
        {
            FinishElevation.RoomBoundary => ceiling ? FinishGeometry.RoomTop(room) : FinishGeometry.RoomBase(room),
            FinishElevation.Slab => slabs!.Find(room, ceiling) + (ceiling ? -thickness : thickness),
            _ => level.Elevation + settings.OffsetMm * FinishGeometry.FeetPerMm
        };
        IList<CurveLoop> loops = FinishGeometry.AtElevation(target.Loops, level.Elevation);
        var elements = new List<Element>();
        if (ceiling && !settings.CeilingAsFloor)
        {
#if REVIT2022_OR_GREATER
            Ceiling created = Ceiling.Create(doc, loops, type.Id, level.Id);
            created.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM).Set(elevation - level.Elevation);
            elements.Add(created);
#else
            throw new InvalidOperationException("В Revit 2019–2021 выберите «Моделировать потолки перекрытиями». Создание системных потолков доступно с Revit 2022.");
#endif
        }
        else
        {
            // The floor offset denotes its top; a ceiling made with a floor must place its bottom at the requested height.
            double offset = elevation - level.Elevation + (ceiling ? thickness : 0);
#if REVIT2022_OR_GREATER
            Floor created = Floor.Create(doc, loops, type.Id, level.Id);
            created.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(offset);
            elements.Add(created);
#else
            foreach (IList<CurveLoop> region in FinishGeometry.Regions(loops))
            {
                CurveArray outer = new();
                foreach (Curve curve in region[0]) outer.Append(curve);
                Floor created = doc.Create.NewFloor(outer, (FloorType)type, level, false);
                created.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(offset);
                doc.Regenerate();
                foreach (CurveLoop hole in region.Skip(1))
                {
                    CurveArray inner = new();
                    foreach (Curve curve in hole) inner.Append(curve);
                    doc.Create.NewOpening(created, inner, true);
                }
                elements.Add(created);
            }
#endif
        }
        return elements;
    }

    public static string Title(FinishKind kind) => kind == FinishKind.Walls ? "Отделка стен" : kind == FinishKind.Floors ? "Отделка полов" : "Отделка потолков";
    private sealed class FinishFailures : IFailuresPreprocessor
    {
        private readonly List<string> warnings;
        public FinishFailures(List<string> warnings) => this.warnings = warnings;
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool error = false;
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                warnings.Add(failure.GetDescriptionText());
                if (failure.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(failure);
                else error = true;
            }
            return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
