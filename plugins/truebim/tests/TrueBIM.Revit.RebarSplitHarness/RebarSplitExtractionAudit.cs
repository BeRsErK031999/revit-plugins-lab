using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Modules.RebarSplit.Revit;

namespace TrueBIM.Revit.RebarSplitHarness;

internal static class RebarSplitExtractionAudit
{
    internal static IEnumerable<object> Run(Document document)
    {
        foreach (string kind in new[] { "Plain", "Rotated", "Mirrored", "Offset", "PinnedGroup", "Ten", "Hundred",
                     "GroupSkipPinPreserve", "GroupExtractPinSkip", "GroupExtractPinCancel" })
        {
            object result;
            try { result = Test(document, kind); }
            catch (Exception exception) { result = new { Scenario = "Extraction_" + kind, Passed = false, Error = exception.ToString() }; }
            yield return result;
        }
    }

    private static object Test(Document document, string kind)
    {
        using TransactionGroup fixture = new(document, "Extraction audit — rollback");
        fixture.Start();
        using RebarSplitNativeFailureCapture failures = new(document);
        List<ElementId> groups = new();
        List<ElementId> selection = new();
        bool independentChoices = kind.StartsWith("Group", StringComparison.Ordinal);
        using (Transaction setup = new(document, "Create extraction fixture"))
        {
            setup.Start();
            Level level = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().OrderBy(item => item.Elevation).First();
            WallType wallType = new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>().First(item => item.Kind == WallKind.Basic);
            wallType.SetCompoundStructure(CompoundStructure.CreateSimpleCompoundStructure(new[]
                { new CompoundStructureLayer(3, MaterialFunctionAssignment.Structure, ElementId.InvalidElementId) }));
            RebarBarType barType = RebarBarType.Create(document);
            barType.BarModelDiameter = 12.0 / 304.8;
            barType.BarNominalDiameter = 12.0 / 304.8;
            Rebar source = Create(40, independentChoices);
            Group first = document.Create.NewGroup(new[] { source.GetHostId(), source.Id });
            groups.Add(first.Id);
            selection.Add(source.Id);
            int count = kind == "Hundred" ? 100 : kind == "Ten" ? 10 : 2;
            document.Regenerate();
            for (int index = 1; index < count; index++)
                groups.Add(document.Create.PlaceGroup(new XYZ(40, 30 * index, level.Elevation), first.GroupType).Id);
            if (kind == "Rotated") ElementTransformUtils.RotateElement(document, first.Id, Line.CreateBound(XYZ.Zero, XYZ.BasisZ), Math.PI / 6);
            if (kind == "Mirrored") ElementTransformUtils.MirrorElements(document, new[] { groups[1] },
                Plane.CreateByNormalAndOrigin(XYZ.BasisX, new XYZ(15, 0, 0)), false);
            if (kind == "Offset") ElementTransformUtils.MoveElement(document, first.Id, new XYZ(12, 17, 0));
            if (kind == "PinnedGroup") first.Pinned = true;
            if (independentChoices)
            {
                selection.Add(Create(200, true).Id);
                selection.Add(Create(250, false).Id);
            }
            Require(setup.Commit() == TransactionStatus.Committed, "Fixture failed to commit.");

            Rebar Create(double x, bool pinned)
            {
                Wall host = Wall.Create(document, Line.CreateBound(new XYZ(x, 0, level.Elevation), new XYZ(x + 30, 0, level.Elevation)),
                    wallType.Id, level.Id, 12, 0, false, true);
                document.Regenerate();
                Rebar bar = Rebar.CreateFromCurves(document, RebarStyle.Standard, barType, null, null, host, XYZ.BasisY,
                    new List<Curve> { Line.CreateBound(new XYZ(x + 2, 0, level.Elevation + 3), new XYZ(x + 8, 0, level.Elevation + 3)) },
                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                bar.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(4, 0.4, true, true, true);
                bar.Pinned = pinned;
                return bar;
            }
        }
        var before = new FilteredElementCollector(document).OfClass(typeof(Rebar)).Cast<Rebar>().ToDictionary(bar => bar.Id, bar => new
        {
            bar.Pinned, bar.GroupId, Host = bar.GetHostId(),
            Curves = RebarSplitGeometry.ExistingPositions(bar).Select(index => RebarSplitGeometry.Curves(bar, index)).ToList(),
            HostCurve = ((LocationCurve)document.GetElement(bar.GetHostId()).Location).Curve.Clone()
        });
        var versions = new FilteredElementCollector(document).WhereElementIsNotElementType().ToDictionary(element => element.Id, element => element.VersionGuid);
        RebarSplitGroupAction groupAction = kind == "GroupSkipPinPreserve" ? RebarSplitGroupAction.Skip : RebarSplitGroupAction.Extract;
        RebarSplitProtectionAction pinAction = kind == "GroupExtractPinSkip" ? RebarSplitProtectionAction.Skip
            : kind == "GroupExtractPinCancel" ? RebarSplitProtectionAction.Cancel : RebarSplitProtectionAction.PreserveStatus;
        int groupDialogs = 0;
        int pinDialogs = 0;
        Stopwatch timer = Stopwatch.StartNew();
        RebarSplitResult result = new RebarSplitService().Execute(document, selection, false, _ => RebarSplitParameterAction.Cancel,
            _ => { pinDialogs++; return pinAction; }, _ => { groupDialogs++; return groupAction; });
        timer.Stop();
        Require(groupDialogs == 1 && pinDialogs == (independentChoices ? 1 : 0), "Independent dialog counts differ.");
        if (pinAction == RebarSplitProtectionAction.Cancel)
        {
            Require(result.Cancelled && versions.Count == new FilteredElementCollector(document).WhereElementIsNotElementType().GetElementCount()
                && versions.All(pair => document.GetElement(pair.Key)?.VersionGuid == pair.Value), "Pin cancellation after group choice changed the model.");
        }
        else
        {
            List<ElementId> processed = before.Where(pair => !(groupAction == RebarSplitGroupAction.Skip && pair.Value.GroupId != ElementId.InvalidElementId)
                && !(pinAction == RebarSplitProtectionAction.Skip && pair.Value.Pinned)).Select(pair => pair.Key).ToList();
            Require(!result.Cancelled && result.SetCount == processed.Count && result.CreatedIds.Count == processed.Count * 4, "Result counts differ.");
            Require(result.SkippedCount == before.Count - processed.Count, "Skipped count differs.");
            foreach (var pair in before)
            {
                Wall host = (Wall)document.GetElement(pair.Value.Host);
                Require(RebarSplitGeometry.SameCurves(new[] { pair.Value.HostCurve }, new[] { ((LocationCurve)host.Location).Curve }), "Host geometry or ID changed.");
                if (!processed.Contains(pair.Key))
                {
                    Rebar original = (Rebar)document.GetElement(pair.Key);
                    Require(original.Pinned == pair.Value.Pinned && original.GroupId == pair.Value.GroupId, "Skipped source status changed.");
                    for (int index = 0; index < pair.Value.Curves.Count; index++)
                        Require(RebarSplitGeometry.SameCurves(pair.Value.Curves[index], RebarSplitGeometry.Curves(original, index)), "Skipped geometry changed.");
                    continue;
                }
                Require(document.GetElement(pair.Key) is null, "Processed source survived.");
                for (int index = 0; index < pair.Value.Curves.Count; index++)
                {
                    Rebar bar = (Rebar)document.GetElement(result.ElementMap.BarPositions[pair.Key][index]);
                    Require(bar.Pinned == pair.Value.Pinned && bar.GroupId == ElementId.InvalidElementId && bar.GetHostId() == pair.Value.Host,
                        "Extracted bar status or host differs.");
                    Require(RebarSplitGeometry.SameCurves(pair.Value.Curves[index], RebarSplitGeometry.Curves(bar, 0)), "Extracted geometry changed.");
                }
            }
            foreach (ElementId id in groups)
            {
                bool extracted = processed.Any(source => before[source].GroupId == id);
                Require((document.GetElement(id) is null) == extracted, "Group extraction differs from the selected choices.");
            }
        }
        Require(failures.Failures.Count == 0, "Unexpected native failures: " + string.Join("; ", failures.Failures.Select(failure => failure.Description)));
        fixture.RollBack();
        return new { Scenario = "Extraction_" + kind, Passed = true, Groups = groups.Count, result.SetCount,
            Bars = result.CreatedIds.Count, result.SkippedCount, ElapsedMilliseconds = timer.ElapsedMilliseconds };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
