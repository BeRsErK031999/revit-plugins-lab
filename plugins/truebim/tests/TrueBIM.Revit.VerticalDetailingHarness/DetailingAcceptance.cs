using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.RebarDetailing;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication
{
    private void TestDetailing(UIDocument ui)
    {
        Document doc = ui.Document;
        RebarDetailingService service = new();
        string ids = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_VIEWS") ?? "2310980,2568691";
        var views = ids.Split(',').Select(s => (View)doc.GetElement(new ElementId(long.Parse(s)))).ToList();
        var snapshot = ModelFingerprint(doc);
        var viewportIds = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Where(p => views.Any(v => v.Id == p.ViewId)).Select(p => p.Id).ToList();
        bool selectionWorks = service.ResolveViews(doc, new List<ElementId>(), views[0]).Single().Id == views[0].Id
            && service.ResolveViews(doc, views.Select(v => v.Id).ToList(), views[0]).Count == views.Count
            && service.ResolveViews(doc, viewportIds, views[0]).Count == views.Count;
        var before = views.ToDictionary(v => v.Id.Value, v => ViewFingerprint(v));
        var preview = CapturePreview(doc, views);
        var tests = new List<object>();
        foreach (View view in views)
        {
            Progress("Testing " + view.Id + " " + view.Name);
            Export(doc, view, "before");
            DetailingGeometry geometry = DetailingGeometry.Read(doc, view);
            DetailingOptions options = new();
            options.TagTypeId = service.PreferredType(new[] { view }, service.Types(doc, "tag"), "tag");
            options.DimensionTypeId = service.PreferredType(new[] { view }, service.Types(doc, "dimension"), "dimension");
            options.SpotTypeId = service.PreferredType(new[] { view }, service.Types(doc, "spot"), "spot");
            options.MultiTypeId = service.PreferredType(Array.Empty<View>(), service.Types(doc, "multi"), "multi");
            var probe = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_PROBE") == "1" ? Probe(doc, view, geometry, options) : null;
            if (probe != null) File.WriteAllText(report + ".probe-" + view.Id + ".json", new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(probe));
            var first = service.Apply(doc, new[] { view }, options).Single();
            Progress("First: " + first);
            Export(doc, view, "after");
            var firstAnnotations = AnnotationFingerprint(view);
            var second = first.Succeeded ? service.Apply(doc, new[] { view }, options).Single() : null;
            bool repeatStable = second != null && firstAnnotations == AnnotationFingerprint(view);
            File.WriteAllText(report + ".fingerprints-" + view.Id + ".json", new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { First = firstAnnotations, Second = AnnotationFingerprint(view) }));
            bool protectedAnnotationSafe = ProtectedAnnotationCheck(doc, view, service, options);
            tests.Add(new { Id = view.Id.Value, view.Name, Options = new { Tag = options.TagTypeId.Value, Dimension = options.DimensionTypeId.Value, Spot = options.SpotTypeId.Value, Multi = options.MultiTypeId.Value },
                Probe = probe,
                Bars = geometry.Bars.Select(b => new { Id = b.Element.Id.Value, b.Element.Name, Class = b.Element.GetType().Name, Link = b.Link?.Id.Value, Count = b.Positions.Count, Target = Point(b.Target), Reference = b.Reference.ConvertToStableRepresentation(doc) }).ToList(),
                Faces = geometry.Faces.Select(f => new { f.HostKey, f.Horizontal, Point = Point(f.Point), Normal = Point(f.Normal) }).ToList(),
                First = first, Second = second, RepeatStable = repeatStable, ProtectedAnnotationSafe = protectedAnnotationSafe,
                ViewUnchanged = before[view.Id.Value] == ViewFingerprint(view),
                VisibleAnnotations = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().Count(e => e.OwnerViewId == view.Id && (e is Dimension || e is IndependentTag)),
                OwnedAnnotations = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().Count(e => e is Dimension || e is IndependentTag),
                TagDiagnostics = TagDiagnostics(doc, view),
                Annotations = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().Where(e => e.OwnerViewId == view.Id).Select(e => Annotation(doc, e)).ToList() });
        }
        Progress("Testing batch and undo");
        var beforeBatch = views.ToDictionary(v => v.Id, AnnotationFingerprint);
        DetailingOptions batchOptions = new()
        {
            TagTypeId = service.PreferredType(views, service.Types(doc, "tag"), "tag"),
            DimensionTypeId = service.PreferredType(views, service.Types(doc, "dimension"), "dimension"),
            SpotTypeId = service.PreferredType(views, service.Types(doc, "spot"), "spot"),
            MultiTypeId = service.PreferredType(views, service.Types(doc, "multi"), "multi")
        };
        bool batchSucceeded;
        using (TransactionGroup batch = new(doc, "Batch and undo acceptance"))
        {
            batch.Start();
            var results = service.Apply(doc, views, batchOptions);
            batchSucceeded = results.Count == views.Count && results.All(r => r.Succeeded);
            batch.RollBack();
        }
        bool batchUndoStable = views.All(v => beforeBatch[v.Id] == AnnotationFingerprint(v));
        Write(new { FatalError = (string?)null, Mode = "apply", Preview = preview, SelectionWorks = selectionWorks,
            BatchSucceeded = batchSucceeded, BatchUndoStable = batchUndoStable, ModelUnchanged = snapshot == ModelFingerprint(doc), Tests = tests,
            PlacedViews = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                .Select(p => new { ViewId = p.ViewId.Value, Number = p.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString(), Sheet = ((ViewSheet)doc.GetElement(p.SheetId)).SheetNumber }).ToList() });
        Progress("Done");
    }

    private object? CapturePreview(Document doc, List<View> views)
    {
        string payload = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_PAYLOAD") ?? "";
        if (!File.Exists(payload)) return null;
        var assembly = System.Reflection.Assembly.LoadFile(payload);
        Type serviceType = assembly.GetType("TrueBIM.App.Modules.RebarDetailing.RebarDetailingService", true)!;
        object service = Activator.CreateInstance(serviceType)!;
        object rows = serviceType.GetMethod("Inspect")!.Invoke(service, new object[] { views });
        Type windowType = assembly.GetType("TrueBIM.App.Modules.RebarDetailing.RebarDetailingWindow", true)!;
        var window = (System.Windows.Window)Activator.CreateInstance(windowType, doc, rows, service)!;
        try
        {
            window.Show(); window.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(report)!, "preview-window.png"))) encoder.Save(file);
            var selected = (IReadOnlyList<View>)windowType.GetProperty("SelectedViews")!.GetValue(window)!;
            return new { Rendered = true, Views = selected.Count, window.ActualWidth, window.ActualHeight, Assembly = assembly.Location };
        }
        finally { window.Close(); }
    }

    private static object Probe(Document doc, View view, DetailingGeometry geometry, DetailingOptions options)
    {
        List<object> items = new();
        using Transaction transaction = new(doc, "Probe references"); transaction.Start();
        foreach (DetailingBar bar in geometry.Bars)
        {
            List<object> references = new();
            foreach (Reference reference in bar.Element.GetSubelements().Select(s => s.GetReference()).Take(2).Concat(new[] { bar.Reference, new Reference(bar.Element) })
                .GroupBy(r => r.ConvertToStableRepresentation(doc)).Select(g => g.First()))
            {
                try
                {
                    IndependentTag tag = IndependentTag.Create(doc, options.TagTypeId, view.Id, reference, true, TagOrientation.Horizontal, view.Origin);
                    references.Add(new { Stable = reference.ConvertToStableRepresentation(doc), Success = true }); doc.Delete(tag.Id);
                }
                catch (Exception ex) { references.Add(new { Stable = reference.ConvertToStableRepresentation(doc), Success = false, Error = ex.Message }); }
            }
            GeometryElement g = bar.Element.get_Geometry(new Options { View = view, ComputeReferences = true, IncludeNonVisibleObjects = true });
            GeometryElement coarse = bar.Element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Coarse, ComputeReferences = true, IncludeNonVisibleObjects = true });
            IList<Curve> center = bar.Element is Rebar rb ? rb.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0)
                : ((RebarInSystem)bar.Element).GetCenterlineCurves(false, false, false);
            items.Add(new { Id = bar.Element.Id.Value, SubCount = bar.Element.GetSubelements().Count, References = references,
                Geometry = g == null ? null : Describe(g).Take(12).ToList(), Coarse = coarse == null ? null : Describe(coarse).Take(12).ToList(),
                Center = center.Select(c => new { Class = c.GetType().Name, Stable = c.Reference?.ConvertToStableRepresentation(doc) }).ToList() });
        }
        transaction.RollBack(); return items;
        IEnumerable<object> Describe(GeometryElement element)
        {
            foreach (GeometryObject item in element)
            {
                if (item is GeometryInstance instance) { foreach (object nested in Describe(instance.GetSymbolGeometry())) yield return nested; }
                else if (item is Curve curve) yield return new { Class = item.GetType().Name, curve.IsBound, Stable = curve.Reference?.ConvertToStableRepresentation(doc),
                    Start = curve.IsBound ? Point(curve.GetEndPoint(0)) : curve is Line line ? Point(line.Origin) : null,
                    End = curve.IsBound ? Point(curve.GetEndPoint(1)) : null, Direction = curve is Line axis ? Point(axis.Direction) : null };
                else yield return new { Class = item.GetType().Name };
            }
        }
    }

    private void Export(Document doc, View view, string suffix)
    {
        try
        {
            ImageExportOptions options = new() { ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(Path.GetDirectoryName(report)!, view.Id + "-" + suffix),
                HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage, PixelSize = 1800, FitDirection = FitDirectionType.Horizontal };
            options.SetViewsAndSheets(new[] { view.Id }); doc.ExportImage(options);
        }
        catch (Exception ex) { Progress("Export: " + ex.Message); }
    }

    private static string ViewFingerprint(View v) => string.Join("|", v.ViewTemplateId, v.Scale, v.CropBoxActive, v.CropBoxVisible,
        v.CropBox.Min.ToString(), v.CropBox.Max.ToString(), v.CropBox.Transform.Origin.ToString(),
        string.Join(",", new FilteredElementCollector(v.Document, v.Id).WhereElementIsNotElementType()
            .Where(e => e.OwnerViewId == v.Id && (e is CurveElement || e is TextNote || e is FamilyInstance))
            .OrderBy(e => e.Id.Value).Select(e => e.UniqueId)));
    private static string AnnotationFingerprint(View v) => string.Join("\n", new FilteredElementCollector(v.Document).OwnedByView(v.Id).WhereElementIsNotElementType()
        .Where(e => e.OwnerViewId == v.Id && (e is Dimension || e is IndependentTag || e is MultiReferenceAnnotation))
        .Select(e => e.GetType().Name + ":" + e.GetTypeId() + ":" + (e is IndependentTag tag ? tag.TagText + ":" + tag.TagHeadPosition + ":" +
                string.Join(",", tag.GetTaggedReferences().Select(r => r.ConvertToStableRepresentation(v.Document)))
            : e is Dimension d ? (d.Curve is Line axis ? axis.Origin.ToString() : "spot") + ":" + d.Value + ":" + d.Prefix + ":" +
                string.Join(",", d.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(v.Document))) + ":" +
                string.Join(",", d.Segments.Cast<DimensionSegment>().Select(s => s.Value + ":" + s.Prefix + ":" + s.TextPosition)) : ""))
        .OrderBy(s => s));
    private static object TagDiagnostics(Document doc, View view)
    {
        var tags = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
        var visible = new HashSet<ElementId>(new FilteredElementCollector(doc, view.Id).ToElementIds());
        HashSet<ElementId> withoutCrop;
        using (Transaction probe = new(doc, "Check tag crop"))
        {
            probe.Start();
            using ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();
            manager.LeftAnnotationCropOffset = manager.RightAnnotationCropOffset = manager.BottomAnnotationCropOffset = manager.TopAnnotationCropOffset = 10;
            doc.Regenerate();
            withoutCrop = new HashSet<ElementId>(new FilteredElementCollector(doc, view.Id).ToElementIds()); probe.RollBack();
        }
        return tags.Select(t => new { t.TagText, t.IsOrphaned, Head = Point(t.TagHeadPosition), Visible = visible.Contains(t.Id), VisibleWithLargeCrop = withoutCrop.Contains(t.Id),
            Min = t.get_BoundingBox(view)?.Min == null ? null : Point(t.get_BoundingBox(view).Min), Max = t.get_BoundingBox(view)?.Max == null ? null : Point(t.get_BoundingBox(view).Max),
            References = t.GetTaggedReferences().Select(r => r.ConvertToStableRepresentation(doc)).ToList() }).ToList();
    }
    private static bool ProtectedAnnotationCheck(Document doc, View view, RebarDetailingService service, DetailingOptions options)
    {
        IndependentTag? tag = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().FirstOrDefault();
        if (tag == null) return false;
        using TransactionGroup group = new(doc, "Protected annotation check"); group.Start();
        using (Transaction pin = new(doc, "Pin one annotation")) { pin.Start(); tag.Pinned = true; pin.Commit(); }
        string before = AnnotationFingerprint(view);
        var result = service.Apply(doc, new[] { view }, options).Single();
        bool safe = !result.Succeeded && before == AnnotationFingerprint(view) && tag.Pinned;
        group.RollBack(); return safe;
    }
    private static string ModelFingerprint(Document doc)
    {
        string text = string.Join("\n", new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e is Rebar || e is RebarInSystem || e is AreaReinforcement || e is Wall || e is Floor || e is FamilyInstance || e is RevitLinkInstance)
            .Where(e => e.OwnerViewId == ElementId.InvalidElementId).OrderBy(e => e.Id.Value)
            .Select(e => e.UniqueId + "|" + e.GetTypeId() + "|" + e.Pinned + "|" + e.GroupId + "|" +
                e.get_BoundingBox(null)?.Min + "|" + e.get_BoundingBox(null)?.Max + "|" +
                string.Join(";", e.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value).Select(p => p.Id + ":" + p.AsValueString() + ":" +
                    (p.StorageType == StorageType.String ? p.AsString() : p.StorageType == StorageType.Double ? p.AsDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        : p.StorageType == StorageType.Integer ? p.AsInteger().ToString() : p.StorageType == StorageType.ElementId ? p.AsElementId().ToString() : "")))));
        using SHA256 hash = SHA256.Create(); return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }
}
