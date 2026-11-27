using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal static class FinishOffLevelContactSource
{
    private const double Thickness = FinishCandidateSearchRules.HorizontalToleranceInternal;

    public static void AddContacts(Document document, FinishQuantityRequest request,
        FinishRoomGeometryCache roomCache, FinishOccurrenceAccumulator contacts)
    {
        HashSet<long> assigned = new(contacts.Build().Select(item => item.ElementId));
        foreach (FinishClassifiedElement item in request.Elements)
        {
            if (item.Category is not (FinishPreviewCategory.Walls or FinishPreviewCategory.Floors)
                || assigned.Contains(item.Element.ElementId) || item.Element.Bounds is not { } bounds) continue;

            // Test actual room volumes. A wall dipping slightly into the storey below
            // must not move there just because the bottom edge touches another room.
            var rooms = request.Rooms.Where(room => room.Bounds is { } box && bounds.Intersects(box, Thickness)
                && (item.Category != FinishPreviewCategory.Walls || FinishCandidateSearchRules.HasWallHeightOverlap(bounds, box))).ToArray();
            if (!rooms.Any(room => room.LevelId != item.Element.LevelId)) continue;
            if (document.GetElement(RevitElementIds.Create(item.Element.ElementId)) is not HostObject host) continue;

            FinishOccurrence? owner = FindOwner(host, item, rooms, roomCache);
            if (owner is not null) contacts.KeepLargest(owner);
        }
    }

    private static FinishOccurrence? FindOwner(HostObject host, FinishClassifiedElement item,
        IEnumerable<FinishRoomCandidateSnapshot> rooms, FinishRoomGeometryCache roomCache)
    {
        List<FaceProbe> probes = [];
        try
        {
            IList<Reference>[] sides = host switch
            {
                Wall => [HostObjectUtils.GetSideFaces(host, ShellLayerType.Interior), HostObjectUtils.GetSideFaces(host, ShellLayerType.Exterior)],
                _ => [HostObjectUtils.GetTopFaces(host)]
            };
            for (int side = 0; side < sides.Length; side++)
            {
                foreach (Reference reference in sides[side])
                {
                    if (host.GetGeometryObjectFromReference(reference) is not Face face) return null;
                    probes.Add(new FaceProbe(face, side));
                }
            }

            FinishOccurrence? owner = null;
            foreach (FinishRoomCandidateSnapshot room in rooms)
            {
                if (!roomCache.TryGet(room.ElementId, out var geometry, out _) || geometry is null) return null;
                double largest = 0;
                foreach (var side in probes.GroupBy(probe => probe.Side))
                {
                    double area = 0, availableArea = 0;
                    foreach (FaceProbe probe in side)
                    {
                        var measure = probe.Measure(geometry, host is Wall ? room.Bounds : null);
                        if (measure is null) return null;
                        area += measure.Value.Contact;
                        availableArea += measure.Value.Available;
                    }
                    if (availableArea > 0 && area > availableArea / 2) largest = Math.Max(largest, area);
                }
                if (largest <= 0) continue;
                if (owner is not null) return null;
                owner = new FinishOccurrence(room.ElementId, item.Element.ElementId, item.Category,
                    RevitAreaUnits.ToSquareMeters(largest), FinishQuantityMethod.FinishFaceRoomContact);
            }
            return owner;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Preserve the unresolved assignment if Revit cannot establish a unique contact.
            return null;
        }
        finally
        {
            foreach (FaceProbe probe in probes) probe.Dispose();
        }
    }

    private static double ContactArea(Solid probe, Solid room)
    {
        Solid intersection;
        try { intersection = BooleanOperationsUtils.ExecuteBooleanOperation(probe, room, BooleanOperationsType.Intersect); }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            intersection = BooleanOperationsUtils.ExecuteBooleanOperation(room, probe, BooleanOperationsType.Intersect);
        }
        using (intersection) return Math.Max(0, intersection.Volume) / Thickness;
    }

    private static double AreaWithinRoomHeight(Solid probe, AxisAlignedBox3D bounds)
    {
        using Plane bottom = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, bounds.MinZ));
        using Plane top = Plane.CreateByNormalAndOrigin(-XYZ.BasisZ, new XYZ(0, 0, bounds.MaxZ));
        using Solid aboveBase = BooleanOperationsUtils.CutWithHalfSpace(probe, bottom);
        using Solid betweenLevels = BooleanOperationsUtils.CutWithHalfSpace(aboveBase, top);
        return Math.Max(0, betweenLevels.Volume) / Thickness;
    }

    private sealed class FaceProbe : IDisposable
    {
        private readonly Face face;
        private readonly Solid? extrusion;
        private List<(XYZ Point, double Z, double Area)>? samples;
        public int Side { get; }

        public FaceProbe(Face face, int side)
        {
            this.face = face;
            Side = side;
            if (face is not PlanarFace plane) return;
            try { extrusion = GeometryCreationUtilities.CreateExtrusionGeometry(face.GetEdgesAsCurveLoops(), plane.FaceNormal, Thickness); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* Use the trimmed face below. */ }
        }

        public (double Contact, double Available)? Measure(FinishRoomGeometryData room, AxisAlignedBox3D? heightBand)
        {
            if (extrusion is not null)
            {
                try
                {
                    double contact = ContactArea(extrusion, room.Solid);
                    // A wall can be taller than the room. Only its face within the actual
                    // room height can contribute to the contact test. Openings stay cut.
                    double available = heightBand is null ? face.Area : AreaWithinRoomHeight(extrusion, heightBand);
                    return (contact, available);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* Revit could not intersect or trim the extrusion. */ }
            }

            // Curved walls and faces whose loop cannot be extruded still have a Revit
            // surface. Return each mesh sample to that surface before testing room membership.
            samples ??= SampleFace();
            if (samples.Count == 0) return null;
            double inside = 0, total = 0;
            foreach (var sample in samples)
            {
                if (heightBand is not null && (sample.Z < heightBand.MinZ || sample.Z > heightBand.MaxZ)) continue;
                total += sample.Area;
                if (room.Room.IsPointInRoom(sample.Point)) inside += sample.Area;
            }
            return (inside, total);
        }

        private List<(XYZ Point, double Z, double Area)> SampleFace()
        {
            List<(XYZ, double, double)> points = [];
            using Mesh mesh = face.Triangulate();
            for (int index = 0; index < mesh.NumTriangles; index++)
            {
                MeshTriangle t = mesh.get_Triangle(index);
                if (!AddTriangle(t.get_Vertex(0), t.get_Vertex(1), t.get_Vertex(2), 0)) return [];
            }
            return points;

            bool AddTriangle(XYZ a, XYZ b, XYZ c, int depth)
            {
                double area = (b - a).CrossProduct(c - a).GetLength() / 2;
                if (area < 1e-10) return true;
                if (depth == 0 || (area > 2.5 && depth < 7))
                {
                    XYZ ab = (a + b) / 2, bc = (b + c) / 2, ca = (c + a) / 2;
                    return AddTriangle(a, ab, ca, depth + 1) && AddTriangle(ab, b, bc, depth + 1)
                        && AddTriangle(ca, bc, c, depth + 1) && AddTriangle(ab, bc, ca, depth + 1);
                }
                using IntersectionResult? surface = face.Project((a + b + c) / 3);
                if (surface is null) return false;
                XYZ point = surface.XYZPoint;
                points.Add((point + face.ComputeNormal(surface.UVPoint) * (Thickness / 2), point.Z, area));
                return true;
            }
        }

        public void Dispose() => extrusion?.Dispose();
    }
}
