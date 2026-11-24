using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.SystemDesignation;

// A view collector can also return objects outside a non-rectangular crop. Intersect its
// candidates with graphics actually emitted by Revit, without traversing linked models.
internal class DesignationViewVisibility(Dictionary<ElementId, Element> candidates) : IExportContext
{
#if REVIT2020_OR_GREATER
    private sealed class ModelGraphics(Dictionary<ElementId, Element> candidates)
        : DesignationViewVisibility(candidates), IModelExportContext
    {
        public RenderNodeAction OnPoint(PointNode node) => RenderNodeAction.Proceed;
    }

    private sealed class ViewGraphics(Document doc, Dictionary<ElementId, Element> candidates)
        : DesignationViewVisibility(candidates), IExportContext2D
    {
        public RenderNodeAction OnElementBegin2D(ElementNode node) =>
            OnElementBegin(node.Document.Equals(doc) ? node.ElementId : ElementId.InvalidElementId);
        public void OnElementEnd2D(ElementNode node) => OnElementEnd(node.ElementId);
        public RenderNodeAction OnFaceEdge2D(FaceEdgeNode node) => RenderNodeAction.Proceed;
        public RenderNodeAction OnFaceSilhouette2D(FaceSilhouetteNode node) => RenderNodeAction.Proceed;
    }
#endif
    private readonly HashSet<ElementId> drawn = [];
    private readonly Stack<ElementId> elements = new();

    public static void Restrict(Document doc, View view, Dictionary<ElementId, Element> candidates)
    {
        if (view is not (View3D or ViewPlan or ViewSection)) return;
#if !REVIT2020_OR_GREATER
        if (view is not View3D)
        {
            // 2019 has no 2D custom exporter. Clip the collected model elements against
            // each crop loop in model coordinates (including split crop regions).
            if (!view.CropBoxActive) return;
            using var manager = view.GetCropRegionShapeManager();
            var normal = view.ViewDirection.Normalize();
            var solids = manager.GetCropShape().Select(loop =>
            {
                var box = view.CropBox;
                double depth = box.Max.Z - box.Min.Z;
                double offset = (box.Transform.OfPoint(box.Min) - loop.First().GetEndPoint(0)).DotProduct(normal);
                using var baseLoop = CurveLoop.CreateViaTransform(loop, Transform.CreateTranslation(normal * offset));
                return GeometryCreationUtilities.CreateExtrusionGeometry(new[] { baseLoop }, normal, Math.Max(depth, 0.01));
            }).ToList();
            try
            {
                var clipped = new HashSet<ElementId>();
                foreach (Solid solid in solids)
                {
                    using var filter = new ElementIntersectsSolidFilter(solid);
                    using var collector = new FilteredElementCollector(doc, candidates.Keys.ToList());
                    clipped.UnionWith(collector.WherePasses(filter).ToElementIds());
                }
                foreach (ElementId id in candidates.Keys.Where(id => !clipped.Contains(id)).ToArray()) candidates.Remove(id);
            }
            finally { foreach (var solid in solids) solid.Dispose(); }
            return;
        }
#endif
#if REVIT2020_OR_GREATER
        DesignationViewVisibility context = view is View3D ? new ModelGraphics(candidates) : new ViewGraphics(doc, candidates);
#else
        var context = new DesignationViewVisibility(candidates);
#endif
        // In 2D Revit 2024 emits element brackets but no line segments when geometry is off.
        using var exporter = new CustomExporter(doc, context)
        {
            IncludeGeometricObjects = view is not View3D,
            ShouldStopOnError = true
        };
        exporter.Export(new[] { view.Id });
        foreach (ElementId id in candidates.Keys.Where(id => !context.drawn.Contains(id)).ToArray()) candidates.Remove(id);
    }

    private void MarkDrawn()
    {
        if (elements.Count > 0 && candidates.ContainsKey(elements.Peek())) drawn.Add(elements.Peek());
    }

    public bool Start() => true;
    public void Finish() { }
    public bool IsCanceled() => false;
    public RenderNodeAction OnViewBegin(ViewNode node) => RenderNodeAction.Proceed;
    public void OnViewEnd(ElementId id) { }
    public RenderNodeAction OnElementBegin(ElementId id)
    {
        elements.Push(id);
        return RenderNodeAction.Proceed;
    }
    public void OnElementEnd(ElementId id) => elements.Pop();
    public RenderNodeAction OnInstanceBegin(InstanceNode node) => RenderNodeAction.Proceed;
    public void OnInstanceEnd(InstanceNode node) { }
    public RenderNodeAction OnLinkBegin(LinkNode node) => RenderNodeAction.Skip;
    public void OnLinkEnd(LinkNode node) { }
    public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;
    public void OnFaceEnd(FaceNode node) { }
    public void OnMaterial(MaterialNode node) { }
    public void OnPolymesh(PolymeshTopology node) => MarkDrawn();
    public void OnLight(LightNode node) { }
    public void OnRPC(RPCNode node) => MarkDrawn();

#if REVIT2020_OR_GREATER
    public RenderNodeAction OnCurve(CurveNode node) => RenderNodeAction.Proceed;
    public RenderNodeAction OnPolyline(PolylineNode node) => RenderNodeAction.Proceed;
    public void OnLineSegment(LineSegment segment) => MarkDrawn();
    public void OnPolylineSegments(PolylineSegments segments) => MarkDrawn();
    public void OnText(TextNode node) => MarkDrawn();
#endif
}
