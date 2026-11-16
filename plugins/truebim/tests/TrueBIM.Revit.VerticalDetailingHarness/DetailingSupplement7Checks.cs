using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.RebarDetailing;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication
{
    private object CheckSupplement7(UIDocument ui, View original, RebarDetailingService service, DetailingVariant variant)
    {
        Progress("Testing Supplement 7 cleanup, bundles, crop and updater restoration");
        object Run(string name, Func<object> check)
        {
            object value;
            try { value = check(); } catch (Exception ex) { value = new { Passed = false, Error = ex.ToString() }; }
            System.IO.File.WriteAllText(report + "." + name + ".json", new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value));
            return value;
        }
        var bundleView = ui.Document.GetElement(new ElementId(3141240L)) as View ?? original;
        var cleanup = Run("cleanup", () => CheckFullCleanup(ui.Document, original, service, variant));
        var bundles = Run("bundles", () => CheckBundles(ui.Document, bundleView, service, variant));
        var crop = Run("crop", () => CheckFittedCrop(ui.Document, original, service, variant));
        var updaters = Run("modplus", () => CheckUpdaterScope(ui, original, service, variant));
        var framing = Run("framing", () => CheckLinkedFraming(ui.Document, original, service, variant));
        bool Passed(object value) => (bool)value.GetType().GetProperty("Passed")!.GetValue(value)!;
        return new { Passed = new[] { cleanup, bundles, crop, updaters, framing }.All(Passed),
            Cleanup = cleanup, Bundles = bundles, Crop = crop, Updaters = updaters, LinkedFraming = framing };
    }

    private static DetailingOptions OptionsFor(Document doc, View view, RebarDetailingService service, DetailingVariant variant) => new()
    {
        Variant = variant, WriteQuantities = false,
        TagTypeId = service.PreferredType(new[] { view }, service.Types(doc, "tag"), "tag"),
        DimensionTypeId = service.PreferredType(new[] { view }, service.Types(doc, "dimension"), "dimension"),
        SpotTypeId = service.PreferredType(new[] { view }, service.Types(doc, "spot"), "spot")
    };

    private object CheckFullCleanup(Document doc, View original, RebarDetailingService service, DetailingVariant variant)
    {
        string model = ModelFingerprint(doc);
        using TransactionGroup scenario = new(doc, "Full cleanup regression"); scenario.Start();
        View view;
        ElementId manualText, manualLine;
        using (Transaction setup = new(doc, "Create manual drafting fixture"))
        {
            setup.Start(); view = (View)doc.GetElement(original.Duplicate(ViewDuplicateOption.WithDetailing));
            DetailingFrame frame = new(view);
            manualText = TextNote.Create(doc, view.Id, frame.Point(frame.BreakBoundary.CenterX, frame.BreakBoundary.CenterY),
                "Ручная подпись QA", new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).FirstElementId()).Id;
            manualLine = doc.Create.NewDetailCurve(view, Line.CreateBound(frame.Point(0, 0), frame.Point(0.1, 0))).Id;
            setup.Commit();
        }
        var options = OptionsFor(doc, view, service, variant);
        options.ConcreteDimensions = options.BarDimensions = options.Elevations = options.Details = options.Pins = false;
        var oldDimensions = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension)).ToElementIds().ToList();
        var selective = service.Apply(doc, new[] { view }, options).Single();
        bool selectiveSafe = selective.Succeeded && oldDimensions.Count > 0 && oldDimensions.All(id => doc.GetElement(id) != null)
            && doc.GetElement(manualText) != null && doc.GetElement(manualLine) != null;
        string before = AnnotationFingerprint(view);
        options.ClearView = true; options.ReplaceExisting = false;
        options.Elevations = true; options.SpotTypeId = ElementId.InvalidElementId;
        var failed = service.Apply(doc, new[] { view }, options).Single();
        bool failureSafe = !failed.Succeeded && before == AnnotationFingerprint(view);
        options.Elevations = false;
        using (Transaction protect = new(doc, "Protect manual detail"))
        { protect.Start(); doc.GetElement(manualLine).Pinned = true; protect.Commit(); }
        var protectedResult = service.Apply(doc, new[] { view }, options).Single();
        bool protectedSafe = !protectedResult.Succeeded && doc.GetElement(manualText) != null && doc.GetElement(manualLine).Pinned;
        using (Transaction unprotect = new(doc, "Restore manual detail protection"))
        { unprotect.Start(); doc.GetElement(manualLine).Pinned = false; unprotect.Commit(); }
        var cleaned = service.Apply(doc, new[] { view }, options).Single();
        var survivors = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().ToList();
        bool cleared = cleaned.Succeeded && cleaned.Removed > 0 && oldDimensions.All(id => doc.GetElement(id) == null)
            && doc.GetElement(manualText) == null && doc.GetElement(manualLine) == null
            // Tags may recreate their own helper dimension even when general
            // dimensioning is off. All pre-existing dimensions must be gone.
            && survivors.OfType<Dimension>().Count() == cleaned.Dimensions
            && !survivors.Any(e => e is SpotDimension || e is AnnotationSymbol || e is DetailCurve
                || e.Category?.Id == new ElementId(BuiltInCategory.OST_DetailComponents))
            && survivors.OfType<IndependentTag>().Any();
        scenario.RollBack();
        return new { Passed = selectiveSafe && failureSafe && protectedSafe && cleared && model == ModelFingerprint(doc),
            SelectiveSafe = selectiveSafe, FailureRollbackSafe = failureSafe, ProtectedSafe = protectedSafe,
            Cleared = cleared, Selective = selective, Failure = failed, Cleaned = cleaned };
    }

    private object CheckBundles(Document doc, View original, RebarDetailingService service, DetailingVariant variant)
    {
        var originalGeometry = DetailingGeometry.Read(doc, original);
        var frame = new DetailingFrame(original);
        var source = originalGeometry.Bars.FirstOrDefault(b => b.Element is Rebar && b.VisibleIndices.Count >= 4
            && !b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999 && s.Start.DistanceTo(s.End) > 500 / 304.8));
        if (source == null) return new { Passed = false, Error = "No transverse rebar array for paired-bar fixture" };
        using TransactionGroup scenario = new(doc, "Paired transverse bars regression"); scenario.Start();
        View view;
        Rebar duplicate;
        double diameter = doc.GetElement(source.Element.GetTypeId()).get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble();
        using (Transaction setup = new(doc, "Add touching pair at each transverse row"))
        {
            setup.Start(); view = (View)doc.GetElement(original.Duplicate(ViewDuplicateOption.Duplicate));
            duplicate = (Rebar)doc.GetElement(ElementTransformUtils.CopyElement(doc, source.Element.Id, frame.Up * diameter).Single());
            var otherTransverse = originalGeometry.Bars.Where(b => b.Element.Id != source.Element.Id
                && !b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Up)) > 0.999 && s.Start.DistanceTo(s.End) > 500 / 304.8))
                .Select(b => b.Element.Id).Where(id => doc.GetElement(id).CanBeHidden(view)).ToList();
            if (otherTransverse.Count > 0) view.HideElements(otherTransverse);
            setup.Commit();
        }
        var options = OptionsFor(doc, view, service, variant);
        options.Elevations = options.ConcreteDimensions = false;
        var result = service.Apply(doc, new[] { view }, options).Single();
        var geometry = DetailingGeometry.Read(doc, view);
        var first = geometry.Bars.Single(b => b.Element.Id == source.Element.Id);
        var upper = geometry.Bars.Single(b => b.Element.Id == duplicate.Id);
        var originalRows = first.Positions.Select(frame.Y).OrderBy(y => y).ToList();
        var vertical = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(d => d is not SpotDimension && d.Curve is Line line && Math.Abs(line.Direction.DotProduct(frame.Up)) > 0.999).ToList();
        var upperReferences = upper.DimensionReferences.SelectMany(a => a.Candidates).Select(r => r.ConvertToStableRepresentation(doc).Replace(":LINEAR", "")).ToList();
        bool lowerOnly = vertical.Count > 0 && vertical.SelectMany(d => d.References.Cast<Reference>())
            .All(r => !upperReferences.Contains(r.ConvertToStableRepresentation(doc).Replace(":LINEAR", "")));
        var pairedIds = new HashSet<ElementId>(new[] { source.Element.Id, duplicate.Id });
        var tags = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
            .Where(t => t.GetTaggedReferences().Any(r => pairedIds.Contains(r.ElementId))).ToList();
        bool oneLeader = tags.Count == 1 && tags[0].HasLeader && tags[0].GetTaggedReferences().Count == 1
            && tags[0].GetTaggedReferences()[0].ElementId == source.Element.Id
            && originalRows.Any(y => Math.Abs(y - frame.Y(tags[0].GetLeaderEnd(tags[0].GetTaggedReferences()[0]))) < 0.1 / 304.8);
        var pins = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Symbol.FamilyName == "(Узел) Арматура • Ш1").Select(f => frame.Y(((LocationPoint)f.Location).Point)).OrderBy(y => y).ToList();
        var expected = originalRows.Where((_, i) => i % 2 == 0).ToList();
        bool alternating = pins.Count == expected.Count && pins.Zip(expected, (a, b) => Math.Abs(a - b) < 0.1 / 304.8).All(ok => ok);
        string beforeRepeat = AnnotationFingerprint(view);
        var repeated = service.Apply(doc, new[] { view }, options).Single();
        bool stable = repeated.Succeeded && beforeRepeat == AnnotationFingerprint(view);
        Export(doc, view, "supplement7-bundles");
        scenario.RollBack();
        return new { Passed = result.Succeeded && lowerOnly && oneLeader && alternating && stable, Result = result,
            LowerReferencesOnly = lowerOnly, OneLeaderToLowerBar = oneLeader, AlternatingBundles = alternating, RepeatStable = stable,
            LowerRowsMm = originalRows.Select(y => y * 304.8).ToList(), PinRowsMm = pins.Select(y => y * 304.8).ToList(),
            TagCount = tags.Count, DiameterMm = diameter * 304.8 };
    }

    private object CheckFittedCrop(Document doc, View original, RebarDetailingService service, DetailingVariant variant)
    {
        using TransactionGroup scenario = new(doc, "Crop expansion and contraction regression"); scenario.Start();
        View view;
        using (Transaction setup = new(doc, "Oversize the original crop"))
        {
            setup.Start(); view = (View)doc.GetElement(original.Duplicate(ViewDuplicateOption.Duplicate));
            var box = view.CropBox; box.Min -= new XYZ(1, 1, 0); box.Max += new XYZ(1, 1, 0); view.CropBox = box; setup.Commit();
        }
        var before = CropCoordinates(view);
        var options = OptionsFor(doc, view, service, variant);
        var result = service.Apply(doc, new[] { view }, options).Single();
        var after = CropCoordinates(view);
        bool shrunk = after[0] > before[0] && after[2] < before[2] && CropDepthPreserved(before, after);
        var frame = new DetailingFrame(view); var geometry = DetailingGeometry.Read(doc, view);
        typeof(RebarDetailingService).GetMethod("PlanBreakBoundary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { frame, geometry, options });
        var baseline = geometry.PlannedBreakBoundary!.Value;
        var crop = view.CropBox;
        double left = frame.X(crop.Transform.OfPoint(crop.Min)), right = frame.X(crop.Transform.OfPoint(crop.Max));
        var margins = new[] { (baseline.Left - left) * 304.8, (right - baseline.Right) * 304.8 };
        bool fitted = margins.All(mm => mm >= view.Scale * 6 - 0.1 && mm <= view.Scale * 6.4 + 0.1);
        using (Transaction small = new(doc, "Undersize the crop without removing reinforcement"))
        {
            small.Start(); crop.Min += new XYZ(100 / 304.8, 0, 0); crop.Max -= new XYZ(100 / 304.8, 0, 0); view.CropBox = crop; small.Commit();
        }
        var expanded = service.Apply(doc, new[] { view }, options).Single();
        bool expands = expanded.Succeeded && after.Zip(CropCoordinates(view), (a, b) => Math.Abs(a - b) < 0.1 / 304.8).All(ok => ok);
        scenario.RollBack();
        return new { Passed = result.Succeeded && shrunk && fitted && expands, Result = result, Shrunk = shrunk,
            FitsMask = fitted, ExpandsBack = expands, BaselineMarginsMm = margins };
    }

    private object CheckUpdaterScope(UIDocument ui, View view, RebarDetailingService service, DetailingVariant variant)
    {
        return ModPlusCompatibilityFixtureApplication.Check(ui, view, service, OptionsFor(ui.Document, view, service, variant));
    }

    private object CheckLinkedFraming(Document doc, View original, RebarDetailingService service, DetailingVariant variant)
    {
        var frame = new DetailingFrame(original); var geometry = DetailingGeometry.Read(doc, original);
        var options = OptionsFor(doc, original, service, variant);
        typeof(RebarDetailingService).GetMethod("PlanBreakBoundary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { frame, geometry, options });
        var boundary = geometry.PlannedBreakBoundary!.Value;
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!, "structural-framing-fixture.rvt");
        using (var linkDoc = doc.Application.NewProjectDocument(UnitSystem.Metric))
        {
            using (Transaction create = new(linkDoc, "Create linked structural framing solid"))
            {
                create.Start();
                var vertices = new[] { frame.Point(boundary.Left - 1, boundary.Bottom - 1), frame.Point(boundary.CenterX, boundary.Bottom - 1),
                    frame.Point(boundary.CenterX, boundary.Bottom + 300 / 304.8), frame.Point(boundary.Left - 1, boundary.Bottom + 300 / 304.8) }
                    .Select(p => p + frame.Normal * (frame.CutDepth - 1)).ToList();
                CurveLoop loop = new();
                for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(vertices[i], vertices[(i + 1) % 4]));
                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, frame.Normal, 2);
                var beam = DirectShape.CreateElement(linkDoc, new ElementId(BuiltInCategory.OST_StructuralFraming));
                beam.SetShape(new GeometryObject[] { solid }); create.Commit();
            }
            linkDoc.SaveAs(path, new SaveAsOptions()); linkDoc.Close(false);
        }
        using TransactionGroup scenario = new(doc, "Linked structural framing regression"); scenario.Start();
        View view;
        RevitLinkInstance link;
        using (Transaction setup = new(doc, "Place linked framing fixture"))
        {
            setup.Start(); view = (View)doc.GetElement(original.Duplicate(ViewDuplicateOption.Duplicate));
            var type = RevitLinkType.Create(doc, new FilePath(path), new RevitLinkOptions(false));
            link = RevitLinkInstance.Create(doc, type.ElementId); setup.Commit();
        }
        var linked = DetailingGeometry.Read(doc, view);
        var beams = linked.Hosts.Where(h => h.Link?.Id == link.Id && h.Element.Category?.Id == new ElementId(BuiltInCategory.OST_StructuralFraming)).ToList();
        bool solidRead = beams.Count == 1 && linked.Solids.Any(s => s.HostKey == beams[0].Key);
        var result = service.Apply(doc, new[] { view }, options).Single();
        var corners = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Symbol.FamilyName == "(Узел) Область обрыва • 2" && f.Location is LocationPoint p
                && Math.Abs(frame.X(p.Point) - boundary.Left) < 0.1 / 304.8 && Math.Abs(frame.Y(p.Point) - boundary.Bottom) < 0.1 / 304.8).Count();
        Export(doc, view, "supplement7-linked-framing"); scenario.RollBack();
        return new { Passed = result.Succeeded && solidRead && corners > 0, Result = result, LinkedSolidRead = solidRead, LowerCornerBreaks = corners,
            Fixture = "Detached linked RVT with native DirectShape solid in Structural Framing category" };
    }
}
