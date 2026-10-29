using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.RebarDetailing;

internal sealed class DetailingBar
{
    public Element Element { get; set; } = null!;
    public RevitLinkInstance? Link { get; set; }
    public Reference Reference { get; set; } = null!;
    public ElementId Host { get; set; } = ElementId.InvalidElementId;
    public List<XYZ> Positions { get; } = new();
    public List<Reference> TagReferences { get; } = new();
    public List<Reference> PositionReferences { get; } = new();
    public HashSet<int> VisibleIndices { get; } = new();
    public XYZ Distribution { get; set; } = XYZ.Zero;
    public List<DetailingAxis> DimensionReferences { get; } = new();
    public List<DetailingAxis> EndReferences { get; } = new();
    public List<DetailingAxis> BendEndReferences { get; } = new();
    public List<DetailingSegment> Segments { get; } = new();
    public double Spacing { get; set; }
    public string Shape { get; set; } = "";
    public string ShapeName { get; set; } = "";
    public XYZ Target => Positions[Positions.Count / 2];
    public string Key { get; set; } = "";
}

internal sealed class DetailingSegment
{
    public XYZ Start { get; set; } = null!;
    public XYZ End { get; set; } = null!;
    public int BarIndex { get; set; }
    public XYZ Direction => (End - Start).Normalize();
}

internal sealed class DetailingHost
{
    public Element Element { get; set; } = null!;
    public RevitLinkInstance? Link { get; set; }
    public string Key => DetailingGeometry.Key(Element.Id, Link);
    public DetailingRect Bounds { get; set; }
}

internal sealed class DetailingAxis
{
    public XYZ Point { get; set; } = null!;
    public XYZ Direction { get; set; } = null!;
    public List<Reference> Candidates { get; set; } = new();
    public Reference Reference => Candidates[0];
}

internal sealed class DetailingFace
{
    public Reference Reference { get; set; } = null!;
    public XYZ Point { get; set; } = null!;
    public XYZ Normal { get; set; } = null!;
    public string HostKey { get; set; } = "";
    public bool Horizontal { get; set; }
    public DetailingRect Bounds { get; set; }
}

internal sealed class DetailingGeometry
{
    public List<DetailingBar> Bars { get; } = new();
    public List<DetailingFace> Faces { get; } = new();
    public List<DetailingHost> Hosts { get; } = new();
    public List<string> Notes { get; } = new();
    public int HostCount { get; private set; }
    public static string Key(ElementId id, RevitLinkInstance? link) => $"{(link == null ? 0 : RevitElementIds.GetValue(link.Id))}:{RevitElementIds.GetValue(id)}";

    public static DetailingGeometry Read(Document doc, View view)
    {
        DetailingGeometry result = new();
        DetailingFrame frame = new(view);
        var visible = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElements();
        result.Add(visible, view, frame, null);
#if REVIT2024_OR_GREATER
        foreach (RevitLinkInstance link in visible.OfType<RevitLinkInstance>())
        {
            if (link.GetLinkDocument() == null) { result.Notes.Add($"Связь «{link.Name}» не загружена."); continue; }
            try
            {
                var linked = new FilteredElementCollector(doc, view.Id, link.Id).WhereElementIsNotElementType().ToElements();
                result.Add(linked, view, frame, link);
            }
            catch (Exception ex) { result.Notes.Add($"Связь «{link.Name}»: {ex.Message}"); }
        }
#else
        if (visible.OfType<RevitLinkInstance>().Any())
            result.Notes.Add("Revit 2019–2023: автоматическое оформление элементов связей недоступно; обработана арматура и бетон текущего проекта.");
#endif
        return result;
    }

    private void Add(IEnumerable<Element> elements, View view, DetailingFrame frame, RevitLinkInstance? link)
    {
        Transform transform = link?.GetTotalTransform() ?? Transform.Identity;
        foreach (Element e in elements)
        {
            if (e is Rebar || e is RebarInSystem)
            {
                if (!frame.Intersects(e.get_BoundingBox(null), transform)) continue;
                try { ReadBar(e, view, frame, link, transform); }
                catch (Exception ex) { Notes.Add($"Арматура {e.Id}: {ex.Message}"); }
            }
            else if (e is Wall || e is Floor || e.Category?.Id == new ElementId(BuiltInCategory.OST_StructuralColumns)
                || e.Category?.Id == new ElementId(BuiltInCategory.OST_StructuralFoundation))
            {
                if (!frame.Intersects(e.get_BoundingBox(null), transform)) continue;
                HostCount++;
                BoundingBoxXYZ box = e.get_BoundingBox(null);
                if (box != null)
                {
                    var corners = new List<XYZ>();
                    for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                        corners.Add(transform.OfPoint(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z))));
                    Hosts.Add(new DetailingHost { Element = e, Link = link,
                        Bounds = new DetailingRect(corners.Min(frame.X), corners.Min(frame.Y), corners.Max(frame.X), corners.Max(frame.Y)) });
                }
                Options options = new() { ComputeReferences = true, IncludeNonVisibleObjects = false };
                if (link == null) options.View = view; else options.DetailLevel = ViewDetailLevel.Fine;
                GeometryElement geometry = e.get_Geometry(options);
                if (geometry != null) ReadGeometry(geometry, transform, Key(e.Id, link), link, frame);
            }
        }
    }

    private void ReadBar(Element e, View view, DetailingFrame frame, RevitLinkInstance? link, Transform transform)
    {
        Rebar? rebar = e as Rebar;
        RebarInSystem? system = e as RebarInSystem;
        int count = rebar?.NumberOfBarPositions ?? system!.NumberOfBarPositions;
        DetailingBar bar = new() { Element = e, Link = link, Host = rebar?.GetHostId() ?? system!.GetHostId() };
        var subelements = e.GetSubelements();
        Dictionary<int, Reference> references = new();
        foreach (Subelement subelement in subelements)
        {
            Reference reference = subelement.GetReference();
#if REVIT2022_OR_GREATER
            try { references[rebar?.GetBarIndexFromReference(reference) ?? system!.GetBarIndexFromReference(reference)] = reference; }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
#else
            references[references.Count] = reference;
#endif
        }
        List<Reference> visibleReferences = new();
        List<Line> referenceLines = new();
        if (link == null)
        {
            GeometryElement referenceGeometry = e.get_Geometry(new Options { View = view, ComputeReferences = true, IncludeNonVisibleObjects = true });
            if (referenceGeometry != null) referenceLines = Lines(referenceGeometry).Where(line => line.Reference != null).ToList();
        }
        if (count > 1 && (rebar == null || rebar.IsRebarShapeDriven()))
        {
            Transform first = rebar?.GetShapeDrivenAccessor().GetBarPositionTransform(0) ?? system!.GetBarPositionTransform(0);
            Transform last = rebar?.GetShapeDrivenAccessor().GetBarPositionTransform(count - 1) ?? system!.GetBarPositionTransform(count - 1);
            bar.Distribution = transform.OfVector(last.Origin - first.Origin);
        }
        for (int index = 0; index < count; index++)
        {
            if (!(rebar?.DoesBarExistAtPosition(index) ?? system!.DoesBarExistAtPosition(index))) continue;
            if (link == null && (rebar?.IsBarHidden(view, index) ?? system!.IsBarHidden(view, index))) continue;
            IList<Curve> curves;
            if (rebar != null)
            {
#if REVIT2022_OR_GREATER
                curves = rebar.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, index);
#else
                Transform placement = rebar.IsRebarShapeDriven() ? rebar.GetShapeDrivenAccessor().GetBarPositionTransform(index) : Transform.Identity;
                curves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, index).Select(c => c.CreateTransformed(placement)).ToList();
#endif
            }
            else
            {
#if REVIT2022_OR_GREATER
                curves = system!.GetTransformedCenterlineCurves(false, false, false, index);
#else
                Transform placement = system!.GetBarPositionTransform(index);
                curves = system.GetCenterlineCurves(false, false, false).Select(c => c.CreateTransformed(placement)).ToList();
#endif
            }
            XYZ? target = null;
            double longest = -1;
            foreach (Curve curve in curves)
            {
                IList<XYZ> points = curve.Tessellate();
                for (int i = 1; i < points.Count; i++)
                {
                    XYZ a = transform.OfPoint(points[i - 1]), b = transform.OfPoint(points[i]);
                    if (!frame.ClipSegment(a, b, out XYZ visibleStart, out XYZ visibleEnd)) continue;
                    if (visibleStart.DistanceTo(visibleEnd) > 1e-8)
                        bar.Segments.Add(new DetailingSegment { Start = visibleStart, End = visibleEnd, BarIndex = index });
                    if (a.DistanceTo(b) <= longest) continue;
                    target = (visibleStart + visibleEnd) / 2; longest = a.DistanceTo(b);
                }
            }
            if (target == null) continue;
            bar.Positions.Add(target);
            bar.VisibleIndices.Add(index);
            Reference tagReference = references.TryGetValue(index, out Reference? candidate) ? candidate! : new Reference(e);
            visibleReferences.Add(link == null ? tagReference : tagReference.CreateLinkReference(link));
            // Hidden reference geometry includes the bar centerlines as well as its outlines.
            // Match the actual 3D centerline, not the stable-reference numbering convention.
            bool dimensionAdded = false;
            bool longitudinal = curves.OfType<Line>().Any(c => Math.Abs(c.Direction.DotProduct(frame.Up)) > 0.999 && c.Length > 500 / 304.8);
            foreach (Line centerline in curves.OfType<Line>().Where(c => c.IsBound)
                .OrderByDescending(c => longitudinal && Math.Abs(c.Direction.DotProduct(frame.Up)) > 0.999).ThenByDescending(c => c.Length))
            {
                XYZ? clipped = frame.Clip(centerline.GetEndPoint(0), centerline.GetEndPoint(1));
                if (clipped == null) continue;
                XYZ projectedDirection = centerline.Direction - frame.Normal * centerline.Direction.DotProduct(frame.Normal);
                var matches = referenceLines.Where(line =>
                {
                    if (Math.Abs(line.Direction.DotProduct(centerline.Direction)) < 0.999999) return false;
                    if ((line.Origin - centerline.Origin).CrossProduct(centerline.Direction).GetLength() < 0.001 / 304.8) return true;
                    // Unobscured area reinforcement may be projected to the host face.
                    // Match its view-plane axis, then validate the native measurement.
                    return projectedDirection.GetLength() > 0.999
                        && (frame.Flatten(line.Origin) - frame.Flatten(centerline.Origin)).CrossProduct(projectedDirection.Normalize()).GetLength() < 0.001 / 304.8;
                }).ToList();
                if (matches.Count == 0) continue;
                var dimensionReferences = matches.Select(line => line.Reference)
                    .OrderBy(reference => reference.ConvertToStableRepresentation(view.Document), StringComparer.Ordinal).ToList();
                if (longitudinal && Math.Abs(centerline.Direction.DotProduct(frame.Right)) > 0.999 && centerline.Length > 50 / 304.8)
                {
                    foreach (int endpoint in new[] { 0, 1 })
                    {
                        XYZ end = centerline.GetEndPoint(endpoint);
                        if (!frame.Contains(end)) continue;
                        var ends = new List<Reference>();
                        foreach (Line match in matches.Where(line => line.IsBound))
                        foreach (int indexOfEnd in new[] { 0, 1 })
                        {
                            if (Math.Abs(frame.X(match.GetEndPoint(indexOfEnd)) - frame.X(end)) > 0.1 / 304.8) continue;
                            try { Reference reference = match.GetEndPointReference(indexOfEnd); if (reference != null) ends.Add(reference); } catch { }
                        }
                        if (ends.Count > 0) bar.BendEndReferences.Add(new DetailingAxis { Point = end, Direction = frame.Up, Candidates = ends });
                    }
                }
                if (frame.Vertical && Math.Abs(centerline.Direction.DotProduct(frame.Up)) > 0.999)
                {
                    foreach (int endpoint in new[] { 0, 1 })
                    {
                        XYZ end = centerline.GetEndPoint(endpoint);
                        if (!frame.Contains(end)) continue;
                        var ends = referenceLines.Where(line => Math.Abs(line.Direction.DotProduct(frame.Up)) < 0.001
                            && Math.Abs(frame.Y(line.Origin) - frame.Y(end)) < 0.1 / 304.8
                            && (frame.Flatten(line.Origin) - frame.Flatten(end)).GetLength() < 100 / 304.8)
                            .Select(line => line.Reference).ToList();
                        foreach (Line match in matches.Where(line => line.IsBound))
                        foreach (int indexOfEnd in new[] { 0, 1 })
                        {
                            if (Math.Abs(frame.Y(match.GetEndPoint(indexOfEnd)) - frame.Y(end)) > 0.1 / 304.8) continue;
                            try { Reference reference = match.GetEndPointReference(indexOfEnd); if (reference != null) ends.Add(reference); } catch { }
                        }
                        if (ends.Count > 0) bar.EndReferences.Add(new DetailingAxis { Point = end, Direction = frame.Right, Candidates = ends });
                    }
                }
                if (Math.Abs(centerline.Direction.DotProduct(frame.Normal)) > 0.999)
                {
                    // A bar cut perpendicular to the view is a dimension point. Revit's
                    // geometry API exposes the same geometry identifier as LINEAR,
                    // whereas native multi-rebar dimensions serialize it without that
                    // suffix. Keep the identifiers and verify the measured axis before use.
                    dimensionReferences = dimensionReferences.Select(reference =>
                    {
                        string stable = reference.ConvertToStableRepresentation(view.Document);
                        return stable.EndsWith(":LINEAR", StringComparison.Ordinal)
                            ? Reference.ParseFromStableRepresentation(view.Document, stable.Substring(0, stable.Length - ":LINEAR".Length)) : reference;
                    }).ToList();
                }
                if (!dimensionAdded)
                {
                    bar.DimensionReferences.Add(new DetailingAxis { Point = clipped, Direction = centerline.Direction, Candidates = dimensionReferences });
                    dimensionAdded = true;
                }
            }
        }
        if (bar.Positions.Count == 0) return;
        bar.Reference = visibleReferences[visibleReferences.Count / 2];
        bar.PositionReferences.AddRange(visibleReferences);
        bar.TagReferences.AddRange(visibleReferences.Concat(references.OrderBy(pair => pair.Key)
            .Select(pair => link == null ? pair.Value : pair.Value.CreateLinkReference(link)))
            .GroupBy(reference => reference.ConvertToStableRepresentation(view.Document)).Select(g => g.First()));
        string parameters = string.Join("|", new[] { BuiltInParameter.ALL_MODEL_MARK, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
            BuiltInParameter.REBAR_NUMBER, BuiltInParameter.REBAR_ELEM_LENGTH, BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS }
            .Select(id => e.get_Parameter(id)).Where(p => p != null).Select(p => p!.AsValueString() ?? p.AsString() ?? ""));
        ElementId shapeId = rebar?.GetShapeId() ?? system!.get_Parameter(BuiltInParameter.REBAR_SHAPE)?.AsElementId() ?? ElementId.InvalidElementId;
        string shape = shapeId.ToString();
        bar.ShapeName = e.Document.GetElement(shapeId)?.Name ?? "";
        double spacing = e.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING)?.AsDouble() ?? 0;
        bar.Spacing = spacing; bar.Shape = shape;
        bar.Key = Key(bar.Host, link) + "|" + e.GetTypeId() + "|" + shape + "|" + Math.Round(spacing, 6) + "|" + parameters;
        Bars.Add(bar);
    }

    private static IEnumerable<Line> Lines(GeometryElement geometry)
    {
        foreach (GeometryObject item in geometry)
        {
            if (item is Line line) yield return line;
            // Rebar geometry is expressed in model coordinates. Instance geometry is not
            // copied because copied primitives would lose their source references.
            else if (item is GeometryInstance instance && instance.Transform.IsIdentity)
                foreach (Line child in Lines(instance.GetSymbolGeometry())) yield return child;
        }
    }

    private void ReadGeometry(GeometryElement geometry, Transform transform, string host, RevitLinkInstance? link, DetailingFrame frame)
    {
        foreach (GeometryObject item in geometry)
        {
            if (item is GeometryInstance instance)
            {
                // Symbol geometry retains the references to the original model faces.
                ReadGeometry(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), host, link, frame);
            }
            else if (item is Solid solid && solid.Volume > 1e-9)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is not PlanarFace plane || face.Reference == null) continue;
                    XYZ normal = transform.OfVector(plane.FaceNormal).Normalize();
                    bool horizontal = Math.Abs(normal.DotProduct(frame.Right)) > 0.999;
                    if (!horizontal && Math.Abs(normal.DotProduct(frame.Up)) < 0.999) continue;
                    XYZ? point = null;
                    var facePoints = new List<XYZ>();
                    foreach (EdgeArray loop in face.EdgeLoops)
                    foreach (Edge edge in loop)
                    {
                        IList<XYZ> vertices = edge.Tessellate();
                        for (int i = 1; i < vertices.Count; i++)
                        {
                            if (!frame.ClipSegment(transform.OfPoint(vertices[i - 1]), transform.OfPoint(vertices[i]), out XYZ start, out XYZ end)) continue;
                            facePoints.Add(start); facePoints.Add(end);
                            point ??= (start + end) / 2;
                        }
                    }
                    // Wide horizontal slab faces can contain the entire crop, with all edges outside.
                    if (point == null)
                    {
                        XYZ center = frame.View.CropBox.Transform.OfPoint((frame.View.CropBox.Min + frame.View.CropBox.Max) / 2);
                        XYZ local = transform.Inverse.OfPoint(center);
                        IntersectionResult projection = plane.Project(local);
                        if (projection != null && frame.Contains(transform.OfPoint(projection.XYZPoint))) point = transform.OfPoint(projection.XYZPoint);
                    }
                    if (point == null) continue;
                    if (facePoints.Count == 0) facePoints.Add(point);
                    Faces.Add(new DetailingFace { Reference = link == null ? face.Reference : face.Reference.CreateLinkReference(link),
                        Point = point, Normal = normal, HostKey = host, Horizontal = horizontal,
                        Bounds = new DetailingRect(facePoints.Min(frame.X), facePoints.Min(frame.Y), facePoints.Max(frame.X), facePoints.Max(frame.Y)) });
                }
            }
        }
    }
}
