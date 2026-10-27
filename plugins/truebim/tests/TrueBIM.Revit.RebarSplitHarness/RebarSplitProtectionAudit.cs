using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Modules.RebarSplit.Revit;

namespace TrueBIM.Revit.RebarSplitHarness;

internal static class RebarSplitProtectionAudit
{
    internal static IEnumerable<object> Run(Document document, string? scenarioFilter = null)
    {
        string? filter = scenarioFilter ?? Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO");
        foreach (string kind in new[] { "Ordinary", "Pinned", "Grouped", "RepeatedGroup", "PinnedGroup",
                     "MixedPinned", "MixedGrouped", "MixedRepeatedGroup", "MixedPinnedGroup",
                     "NestedGroup", "RebarOnlyGroup", "RotatedRepeatedGroup", "NestedRepeatedGroup",
                     "MultiSetRepeatedGroup", "MirroredRepeatedGroup", "PinnedMemberRepeatedGroup" }
                     .Where(kind => string.IsNullOrEmpty(filter) || kind == filter))
        foreach (bool annotations in new[] { false, true })
        foreach (RebarSplitProtectionAction action in Enum.GetValues(typeof(RebarSplitProtectionAction)))
        {
            string scenario = kind + "_" + action + (annotations ? "_AnnotationsOn" : "_AnnotationsOff");
            string? report = Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_HARNESS_REPORT");
            if (!string.IsNullOrWhiteSpace(report)) System.IO.File.AppendAllText(report + ".progress.txt", "\n" + scenario);
            object result;
            try { result = Test(document, kind, annotations, scenario, action); }
            catch (Exception exception) { result = new { Scenario = scenario, Passed = false, Error = exception.ToString() }; }
            yield return result;
        }
    }

    private static object Test(Document document, string kind, bool annotations, string scenario, RebarSplitProtectionAction action)
    {
        using RebarSplitNativeFailureCapture native = new(document);
        using TransactionGroup fixture = new(document, "Rebar protection regression — rollback");
        fixture.Start();
        List<ElementId> sources = new();
        using (Transaction setup = new(document, "Create protected rebar fixture"))
        {
            setup.Start();
            Level level = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(item => item.Elevation).First();
            WallType wallType = new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>()
                .First(item => item.Kind == WallKind.Basic);
            wallType.SetCompoundStructure(CompoundStructure.CreateSimpleCompoundStructure(new[]
                { new CompoundStructureLayer(3, MaterialFunctionAssignment.Structure, ElementId.InvalidElementId) }));
            RebarBarType barType = RebarBarType.Create(document);
            barType.BarModelDiameter = 12.0 / 304.8;
            barType.BarNominalDiameter = 12.0 / 304.8;
            // Mixed cases put an ordinary set first to detect partial processing before the guard.
            int count = kind.StartsWith("Mixed", StringComparison.Ordinal) ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                double x = i * 50;
                Wall host = Wall.Create(document,
                    Line.CreateBound(new XYZ(x, 0, level.Elevation), new XYZ(x + 30, 0, level.Elevation)),
                    wallType.Id, level.Id, 12, 0, false, true);
                document.Regenerate();
                Rebar source = Rebar.CreateFromCurves(document, RebarStyle.Standard, barType, null, null, host,
                    XYZ.BasisY, new List<Curve> { Line.CreateBound(new XYZ(x + 2, 0, level.Elevation + 3),
                        new XYZ(x + 8, 0, level.Elevation + 3)) },
                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                source.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(4, 0.4, true, true, true);
                source.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("Protection test " + i);
                source.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("Keep original");
                sources.Add(source.Id);
                if (i != count - 1 || kind == "Ordinary") continue;
                if (kind.EndsWith("Pinned", StringComparison.Ordinal)) source.Pinned = true;
                else
                {
                    List<ElementId> groupMembers = kind == "RebarOnlyGroup" ? new() { source.Id } : new() { host.Id, source.Id };
                    if (kind == "MultiSetRepeatedGroup")
                    {
                        Rebar untouched = Rebar.CreateFromCurves(document, RebarStyle.Standard, barType, null, null, host,
                            XYZ.BasisY, new List<Curve> { Line.CreateBound(new XYZ(x + 12, 0, level.Elevation + 4),
                                new XYZ(x + 18, 0, level.Elevation + 4)) },
                            RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                        untouched.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(4, 0.4, true, true, true);
                        untouched.MoveBarInSet(1, Transform.CreateTranslation(new XYZ(0.25, 0.1, 0.2)));
                        untouched.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("Do not split");
                        groupMembers.Add(untouched.Id);
                    }
                    if (kind == "PinnedMemberRepeatedGroup") source.Pinned = true;
                    document.Regenerate();
                    Group group = document.Create.NewGroup(groupMembers);
                    group.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("Keep group comment");
                    group.GroupType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS)?.Set("Keep group type comment");
                    if (kind.EndsWith("PinnedGroup", StringComparison.Ordinal)) group.Pinned = true;
                    if (kind == "RotatedRepeatedGroup")
                        ElementTransformUtils.RotateElement(document, group.Id,
                            Line.CreateBound(XYZ.Zero, XYZ.BasisZ), Math.PI / 6);
                    if (kind.StartsWith("Nested", StringComparison.Ordinal))
                    {
                        Wall outerMember = Wall.Create(document,
                            Line.CreateBound(new XYZ(50, 0, level.Elevation), new XYZ(70, 0, level.Elevation)),
                            wallType.Id, level.Id, 12, 0, false, true);
                        document.Regenerate();
                        group = document.Create.NewGroup(new[] { group.Id, outerMember.Id });
                    }
                    if (kind.EndsWith("RepeatedGroup", StringComparison.Ordinal))
                    {
                        document.Regenerate();
                        Group copy = document.Create.PlaceGroup(new XYZ(0, 30, level.Elevation), group.GroupType);
                        if (kind == "MirroredRepeatedGroup")
                            ElementTransformUtils.MirrorElements(document, new[] { copy.Id },
                                Plane.CreateByNormalAndOrigin(XYZ.BasisX, new XYZ(15, 0, 0)), false);
                    }
                }
            }
            Assert(setup.Commit() == TransactionStatus.Committed, "Fixture did not commit: "
                + string.Join("; ", native.Failures.Select(failure => failure.Description)));
        }

        RebarSplitService service = new(text => System.IO.File.AppendAllText(
            Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_HARNESS_REPORT") + ".diagnostics.txt", scenario + ": " + text + "\n"));
        Assert(service.FindCandidates(document, sources, RebarSplitScope.Selection).SequenceEqual(sources),
            "Protected sets must not be silently filtered out.");
        HashSet<ElementId> beforeIds = AllElementIds(document);
        var beforeBars = new FilteredElementCollector(document).OfClass(typeof(Rebar)).Cast<Rebar>()
            .ToDictionary(bar => bar.Id, bar => new
            {
                bar.Pinned, bar.GroupId, bar.Quantity, Host = bar.GetHostId(),
                Mark = bar.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString(),
                Comments = bar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString(),
                Geometry = RebarSplitGeometry.ExistingPositions(bar)
                    .Select(index => RebarSplitGeometry.Curves(bar, index)).ToList()
            });
        var beforeGroups = new FilteredElementCollector(document).OfClass(typeof(Group)).Cast<Group>()
            .ToDictionary(group => group.Id, group => new { group.Pinned, group.GroupId, Type = group.GetTypeId(), Members = group.GetMemberIds().ToList(),
                Comment = group.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString(),
                TypeComment = group.GroupType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS)?.AsString() });
        List<ElementId> expandedSources = sources.SelectMany(id =>
        {
            ElementId groupId = beforeBars[id].GroupId;
            if (groupId == ElementId.InvalidElementId) return new[] { id }.AsEnumerable();
            var originalGroup = beforeGroups[groupId];
            int slot = originalGroup.Members.IndexOf(id);
            return beforeGroups.Values.Where(group => group.Type == originalGroup.Type).Select(group => group.Members[slot]);
        }).Distinct().ToList();
        int parameterDialogs = 0;
        int protectionDialogs = 0;
        int groupDialogs = 0;
        List<ElementId> protectedIds = expandedSources.Where(id => beforeBars[id].Pinned || beforeBars[id].GroupId != ElementId.InvalidElementId).ToList();
        RebarSplitResult split = service.Execute(document, sources, annotations, _ =>
        {
            parameterDialogs++;
            return RebarSplitParameterAction.SplitAnyway;
        }, issues =>
        {
            protectionDialogs++;
            List<ElementId> pinnedIds = expandedSources.Where(id => beforeBars[id].Pinned
                && (action != RebarSplitProtectionAction.Skip || beforeBars[id].GroupId == ElementId.InvalidElementId)).ToList();
            Assert(issues.Select(issue => (int)issue.SourceId).OrderBy(id => id)
                .SequenceEqual(pinnedIds.Select(id => id.IntegerValue).OrderBy(id => id)), "Protection dialog IDs differ.");
            foreach (var issue in issues)
            {
                var before = beforeBars[new ElementId((int)issue.SourceId)];
                Assert(issue.Pinned == before.Pinned, "Dialog pin status differs.");
                Assert(issue.GroupId == (before.GroupId == ElementId.InvalidElementId ? null : (long?)before.GroupId.IntegerValue),
                    "Dialog group ID differs.");
            }
            return action;
        }, issues =>
        {
            groupDialogs++;
            Assert(issues.Select(issue => (int)issue.SourceId).OrderBy(id => id).SequenceEqual(expandedSources
                .Where(id => beforeBars[id].GroupId != ElementId.InvalidElementId).Select(id => id.IntegerValue).OrderBy(id => id)),
                "Group dialog IDs differ.");
            return action switch
            {
                RebarSplitProtectionAction.Cancel => RebarSplitGroupAction.Cancel,
                RebarSplitProtectionAction.Skip => RebarSplitGroupAction.Skip,
                _ => RebarSplitGroupAction.Extract
            };
        });
        bool blocked = split.Cancelled;
        Assert(parameterDialogs == 0, "Unexpected parameter dialog.");
        bool hasGroups = expandedSources.Any(id => beforeBars[id].GroupId != ElementId.InvalidElementId);
        bool hasPins = expandedSources.Any(id => beforeBars[id].Pinned
            && (action != RebarSplitProtectionAction.Skip || beforeBars[id].GroupId == ElementId.InvalidElementId));
        Assert(groupDialogs == (hasGroups ? 1 : 0), "Group dialog count differs.");
        Assert(protectionDialogs == (hasPins && !(hasGroups && action == RebarSplitProtectionAction.Cancel) ? 1 : 0), "Protection dialog count differs.");
        bool cancelled = kind != "Ordinary" && action == RebarSplitProtectionAction.Cancel;
        bool skippedProtected = kind != "Ordinary" && action == RebarSplitProtectionAction.Skip;
        List<ElementId> processed = cancelled ? new() : expandedSources.Where(id => !skippedProtected || !protectedIds.Contains(id)).ToList();
        Assert(split.Cancelled == cancelled && split.SetCount == processed.Count && split.CreatedIds.Count == processed.Count * 4,
            "Split result differs: " + string.Join("; ", native.Failures.Select(failure => failure.Description)));
        Assert(split.SkippedCount == (skippedProtected ? protectedIds.Count : 0), "Skipped count differs.");
        foreach (ElementId sourceId in processed)
        {
            Assert(document.GetElement(sourceId) is null, "Source was not deleted.");
            var before = beforeBars[sourceId];
            List<IList<Curve>> expected = before.Geometry.ToList();
            List<Rebar> bars = split.ElementMap[sourceId].Select(document.GetElement).Cast<Rebar>().ToList();
            Assert(bars.Count == 4, "Wrong new bar count for source.");
            foreach (Rebar bar in bars)
            {
                bool preserve = action != RebarSplitProtectionAction.RemoveStatus;
                Assert(bar.GetHostId() == split.ElementMap.Single(before.Host), "New bar host differs.");
                Assert(bar.Pinned == (preserve && before.Pinned) && bar.Quantity == 1, "New bar pin or quantity differs.");
                Assert(bar.GroupId == ElementId.InvalidElementId,
                    "New bar group status differs.");
                Assert(bar.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString() == before.Mark
                    && bar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() == before.Comments,
                    "New bar parameters differ.");
                int match = expected.FindIndex(curves => RebarSplitGeometry.SameCurves(curves, RebarSplitGeometry.Curves(bar, 0)));
                Assert(match >= 0, "Bar geometry changed.");
                expected.RemoveAt(match);
            }
            Assert(expected.Count == 0, "Bar is missing.");
        }
        if (cancelled || processed.Count == 0)
        {
            Assert(beforeIds.SetEquals(AllElementIds(document)),
                "Blocked operation created or deleted elements.");
        }
        foreach (var pair in beforeBars.Where(pair => !processed.Contains(pair.Key)))
            {
                Rebar bar = (Rebar)document.GetElement(split.ElementMap.Single(pair.Key));
                var before = pair.Value;
                bool regrouped = processed.Any(id => beforeBars[id].GroupId == before.GroupId && before.GroupId != ElementId.InvalidElementId);
                Assert(bar.Pinned == before.Pinned && (regrouped ? bar.GroupId == ElementId.InvalidElementId : bar.GroupId == before.GroupId) && bar.Quantity == before.Quantity
                    && bar.GetHostId() == split.ElementMap.Single(before.Host), "Rebar status, group, quantity or host changed.");
                Assert(bar.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString() == before.Mark
                    && bar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() == before.Comments,
                    "Original parameters changed.");
                for (int i = 0; i < before.Geometry.Count; i++)
                    Assert(RebarSplitGeometry.SameCurves(before.Geometry[i], RebarSplitGeometry.Curves(bar, i)),
                        "Original geometry changed.");
            }
        HashSet<ElementId> affectedGroups = new(processed.Select(id => beforeBars[id].GroupId).Where(id => id != ElementId.InvalidElementId));
        foreach (ElementId id in affectedGroups.ToList())
        {
            ElementId parent = beforeGroups[id].GroupId;
            while (parent != ElementId.InvalidElementId && affectedGroups.Add(parent)) parent = beforeGroups[parent].GroupId;
        }
        foreach (var pair in beforeGroups)
        {
            if (affectedGroups.Contains(pair.Key))
            {
                List<ElementId> unchangedMembers = pair.Value.Members.Where(id => !processed.Contains(id) && !affectedGroups.Contains(id)).ToList();
                Assert(unchangedMembers.All(id => document.GetElement(split.ElementMap.Single(id)) is not null), "Other group member disappeared.");
                Assert(document.GetElement(pair.Key) is null && split.ElementMap[pair.Key].Count == 0, "Affected group was not dissolved.");
                Assert(unchangedMembers.All(id => document.GetElement(id).GroupId == ElementId.InvalidElementId), "Other members were not extracted.");
            }
            else
            {
                Group group = (Group)document.GetElement(pair.Key);
                Assert(group.Pinned == pair.Value.Pinned && group.GetTypeId() == pair.Value.Type
                    && group.GetMemberIds().SequenceEqual(pair.Value.Members), "Group membership, type or pin changed.");
            }
        }
        Assert(native.Failures.Count == 0, "Unexpected native failures: " + string.Join("; ", native.Failures.Select(failure => failure.Description)));
        fixture.RollBack();
        return new { Scenario = scenario, Passed = true, Blocked = blocked, Sources = sources.Count,
            ExpandedSources = expandedSources.Count, Groups = beforeGroups.Count, SetCount = split.SetCount, Bars = split.CreatedIds.Count,
            NativeFailures = native.Failures };
    }

    private static HashSet<ElementId> AllElementIds(Document document) => new(
        new FilteredElementCollector(document).WhereElementIsNotElementType().ToElementIds()
            .Concat(new FilteredElementCollector(document).WhereElementIsElementType().ToElementIds()));

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
