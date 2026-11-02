using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarDetailing;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication
{
    private object CheckTemplateToken(Document doc, View original)
    {
        Progress("Testing callout volume token with temporary link name");
        string before = ModelFingerprint(doc);
        using TransactionGroup scenario = new(doc, "Callout volume token acceptance"); scenario.Start();
        View view;
        var geometry = DetailingGeometry.Read(doc, original);
        var link = geometry.Hosts.Select(h => h.Link).First(l => l != null)!;
        using (Transaction setup = new(doc, "Create token fixture"))
        {
            setup.Start(); view = (View)doc.GetElement(original.Duplicate(ViewDuplicateOption.Duplicate));
            ((RevitLinkType)doc.GetElement(link.GetTypeId())).Name = "QA_КЖ02.2_rvt24.rvt";
            setup.Commit();
        }
        RebarDetailingService service = new();
        var options = new DetailingOptions { Tags = false, ConcreteDimensions = false, BarDimensions = false, Elevations = false,
            WriteQuantities = false, Pins = false, UpperNoteBottom = "см. {том}", LowerNoteBottom = "см. {том}" };
        var result = service.Apply(doc, new[] { view }, options).Single();
        var texts = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f is AnnotationSymbol && RebarDetailingService.OwnedDetail(f))
            .Select(f => f.LookupParameter("Нижняя строка").AsString()).ToList();
        bool resolved = result.Succeeded && texts.Count >= 2 && texts.All(t => t == "см. КЖ02.2");
        scenario.RollBack();
        bool unchanged = before == ModelFingerprint(doc);
        return new { Passed = resolved && unchanged, Resolved = resolved, ModelUnchanged = unchanged, Texts = texts,
            Fixture = "Temporary linked type display name; original link name restored" };
    }

    private object MeasureLayout(Document doc, View view)
    {
        DetailingFrame frame = new(view);
        var geometry = DetailingGeometry.Read(doc, view);
        var sections = (List<DetailingSection>)typeof(RebarDetailingService)
            .GetMethod("Sections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { frame, geometry, new DetailingOptions() })!;
        var dimensions = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
        return sections.Select(section =>
        {
            var boundary = (DetailingRect)typeof(RebarDetailingService)
                .GetMethod("SectionAnnotationBoundary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { doc, view, frame, section })!;
            return new { section.HostKey, BreakLeft = boundary.Left,
                VerticalDimensionOffsetsMm = dimensions.Where(d => d is not SpotDimension && d.Curve is Line line && Math.Abs(line.Direction.DotProduct(frame.Up)) > 0.999)
                    .Select(d => Math.Round((boundary.Left - frame.X(((Line)d.Curve).Origin)) * 304.8 / view.Scale, 3)).Distinct().OrderBy(x => x).ToList(),
                SpotEndOffsetsMm = dimensions.OfType<SpotDimension>().Select(d => Math.Round((boundary.Left - frame.X(d.LeaderEndPosition)) * 304.8 / view.Scale, 3)).ToList() };
        }).ToList();
    }

    private object CheckLowerBreakMirroring(Document doc, View original)
    {
        Progress("Testing native corner breaks against actual transient solid intersections");
        var results = new List<object>();
        bool passed = true;
        foreach (bool shortArms in new[] { false, true })
        foreach (bool onRight in new[] { false, true })
        foreach (bool onTop in new[] { false, true })
        {
            using TransactionGroup scenario = new(doc, "Lower break mirror acceptance"); scenario.Start();
            View view;
            using (Transaction duplicate = new(doc, "Create lower break fixture"))
            {
                duplicate.Start(); view = (View)doc.GetElement(original.Duplicate(ViewDuplicateOption.Duplicate)); duplicate.Commit();
            }
            DetailingFrame frame = new(view);
            DetailingGeometry geometry = DetailingGeometry.Read(doc, view);
            DetailingOptions options = new();
            var boundary = frame.BreakBoundary;
            if (shortArms)
            {
                boundary = new DetailingRect(boundary.Left, boundary.Bottom, boundary.Left + 500 / 304.8, boundary.Bottom + 1200 / 304.8);
                geometry.PlannedBreakBoundary = boundary;
            }
            double verticalArm = (shortArms ? 499.872 : 300) / 304.8;
            double left = onRight ? boundary.CenterX : boundary.Left - 1;
            double right = onRight ? boundary.Right + 1 : boundary.CenterX;
            double bottom = onTop ? boundary.Top - verticalArm : boundary.Bottom - 1;
            double top = onTop ? boundary.Top + 1 : boundary.Bottom + verticalArm;
            var vertices = new[] { frame.Point(left, bottom), frame.Point(right, bottom), frame.Point(right, top), frame.Point(left, top) }
                .Select(p => p + frame.Normal * (frame.CutDepth - 1)).ToList();
            CurveLoop loop = new();
            for (int index = 0; index < 4; index++) loop.Append(Line.CreateBound(vertices[index], vertices[(index + 1) % 4]));
            geometry.Solids.Clear();
            geometry.Solids.Add(new DetailingSolid { HostKey = "fixture", Transform = Transform.Identity,
                Solid = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, frame.Normal, 2) });
            var result = new DetailingResult();
            using (Transaction create = new(doc, "Place native one-sided break"))
            {
                create.Start();
                create.SetFailureHandlingOptions(create.GetFailureHandlingOptions().SetFailuresPreprocessor(new FamilyProbeFailures()).SetClearAfterRollback(true));
                typeof(RebarDetailingService).GetMethod("Details", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, new object[] { doc, view, frame, geometry, options, result });
                create.Commit();
            }
            var breaks = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(f => f.Symbol.FamilyName == "(Узел) Область обрыва • 2").ToList();
            var axes = breaks.Select(f => new { Id = f.Id.Value, Horizontal = f.GetTransform().BasisY.DotProduct(frame.Right),
                Vertical = f.GetTransform().BasisX.DotProduct(frame.Up), f.Mirrored,
                PlacementX = frame.X(((LocationPoint)f.Location).Point) }).ToList();
            var path = geometry.Breaks(frame).Single();
            bool correct = axes.Count == 1 && path.Count == 3 && breaks.All(f =>
                Math.Abs(f.LookupParameter("Длина линии 1").AsDouble() - frame.Point(path[0].X, path[0].Y).DistanceTo(frame.Point(path[1].X, path[1].Y))) < 0.1 / 304.8
                && Math.Abs(f.LookupParameter("Длина линии 2").AsDouble() - Math.Max(frame.Paper(6), frame.Point(path[1].X, path[1].Y).DistanceTo(frame.Point(path[2].X, path[2].Y)))) < 0.1 / 304.8
                && frame.Flatten(((LocationPoint)f.Location).Point).DistanceTo(frame.Point(path[1].X, path[1].Y)) < 0.1 / 304.8
                && f.GetTransform().BasisX.Normalize().DotProduct((frame.Point(path[0].X, path[0].Y) - frame.Point(path[1].X, path[1].Y)).Normalize()) > 0.999
                && (-f.GetTransform().BasisY).Normalize().DotProduct((frame.Point(path[2].X, path[2].Y) - frame.Point(path[1].X, path[1].Y)).Normalize()) > 0.999);
            passed &= correct;
            Export(doc, view, (shortArms ? "short-" : "") + (onTop ? "upper" : "lower") + (onRight ? "-break-right" : "-break-left"));
            results.Add(new { ShortArms = shortArms, OnRight = onRight, OnTop = onTop, Passed = correct, Axes = axes });
            scenario.RollBack();
        }
        return new { Passed = passed, Fixture = "Transient native solids intersect the perimeter on two sides; source model unchanged", Cases = results };
    }

    private object ProbeNoteLeaders(Document doc, View view)
    {
        DetailingFrame frame = new(view);
        var results = new List<object>();
        var ids = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(n => n is AnnotationSymbol && RebarDetailingService.OwnedDetail(n)).Select(n => n.Id).ToList();
        foreach (int strategy in new[] { 0, 1, 2, 3 })
        {
            using TransactionGroup trial = new(doc, "Annotation leader diagnostic"); trial.Start();
            using (Transaction change = new(doc, "Leader placement " + strategy))
            {
                change.Start();
                foreach (var id in ids)
                {
                    AnnotationSymbol note = (AnnotationSymbol)doc.GetElement(id);
                    XYZ head = ((LocationPoint)note.Location).Point;
                    XYZ elbow = head - frame.Right * (note.LookupParameter("Длина полки").AsDouble() * view.Scale / 2);
                    foreach (Leader leader in note.GetLeaders())
                    {
                        XYZ target = leader.End;
                        if (strategy == 0)
                        {
                            leader.End = elbow - frame.Right * frame.Paper(10); doc.Regenerate();
                            leader.Elbow = elbow; doc.Regenerate(); leader.End = target;
                        }
                        else if (strategy == 1)
                        {
                            leader.Elbow = head - frame.Right * frame.Paper(80); doc.Regenerate();
                            leader.End = target; doc.Regenerate(); leader.Elbow = elbow;
                        }
                        else if (strategy == 2)
                        {
                            leader.Elbow = leader.Anchor;
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(note.LookupParameter("Нижняя строка").AsString()))
                                note.LookupParameter("Нижняя строка").Set("\u00a0");
                            doc.Regenerate(); leader.End = target; leader.Elbow = elbow;
                        }
                    }
                }
                change.Commit();
            }
            Export(doc, view, "leader-probe-" + strategy);
            results.Add(new { Strategy = strategy, Notes = ids.Select(id => Annotation(doc, doc.GetElement(id))).ToList() });
            trial.RollBack();
        }
        return results;
    }

    private object? CheckSixBars(Document doc, View original, RebarDetailingService service, DetailingVariant variant)
    {
        DetailingFrame frame = new(original);
        DetailingBar? source = DetailingGeometry.Read(doc, original).Bars.FirstOrDefault(b => b.Element is Rebar
            && b.Link == null && b.VisibleIndices.Count >= 8
            && !b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999));
        if (source == null) return null;
        Progress("Testing six irregular bars on disposable view");
        string before = CommentsFingerprint(doc);
        using TransactionGroup scenario = new(doc, "Six irregular bars acceptance"); scenario.Start();
        View view;
        Rebar rebar = (Rebar)source.Element;
        int[] indices = source.VisibleIndices.OrderBy(i => i).ToArray();
        var kept = new HashSet<int>(new[] { indices[0], indices[1], indices[3], indices[5], indices[indices.Length - 3], indices[indices.Length - 1] });
        using (Transaction setup = new(doc, "Show six unevenly spaced bars"))
        {
            setup.Start(); view = (View)doc.GetElement(original.Duplicate(ViewDuplicateOption.Duplicate));
            rebar.SetPresentationMode(view, RebarPresentationMode.Select);
            for (int i = 0; i < rebar.NumberOfBarPositions; i++)
                if (rebar.DoesBarExistAtPosition(i)) rebar.SetBarHiddenStatus(view, i, !kept.Contains(i));
            rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("");
            setup.Commit();
        }
        DetailingOptions options = new()
        {
            Variant = variant, WriteQuantities = false,
            TagTypeId = service.PreferredType(new[] { view }, service.Types(doc, "tag"), "tag"),
            DimensionTypeId = service.PreferredType(new[] { view }, service.Types(doc, "dimension"), "dimension"),
            SpotTypeId = service.PreferredType(new[] { view }, service.Types(doc, "spot"), "spot"),
            MultiTypeId = service.PreferredType(Array.Empty<View>(), service.Types(doc, "multi"), "multi")
        };
        var disabled = service.Apply(doc, new[] { view }, options).Single();
        bool disabledSafe = disabled.Succeeded && string.IsNullOrEmpty(rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString());
        options.WriteQuantities = true;
        var counted = service.Apply(doc, new[] { view }, options).Single();
        int visible = DetailingGeometry.Read(doc, view).Bars.Single(b => b.Element.Id == rebar.Id).VisibleIndices.Count;
        string countText = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString();
        var tags = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().Select(t => t.TagText).ToList();
        bool countSafe = counted.Succeeded && visible == 6 && countText == "6 шт." && tags.Any(t => t.Contains("6 шт."));
        Export(doc, view, "six-irregular");
        using (Transaction protect = new(doc, "Protect counted bar text"))
        {
            protect.Start(); rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("Текст QA Ё вместо количества"); protect.Commit();
        }
        var protectedResult = service.Apply(doc, new[] { view }, options).Single();
        bool textSafe = protectedResult.Succeeded && rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() == "Текст QA Ё вместо количества";
        scenario.RollBack();
        bool rollbackSafe = CommentsFingerprint(doc) == before;
        return new { Passed = countSafe && disabledSafe && textSafe && rollbackSafe, SourceId = rebar.Id.Value,
            Visible = visible, CountText = countText, Tags = tags, CountSafe = countSafe, DisabledSafe = disabledSafe, TextSafe = textSafe, RollbackSafe = rollbackSafe };
    }
}
