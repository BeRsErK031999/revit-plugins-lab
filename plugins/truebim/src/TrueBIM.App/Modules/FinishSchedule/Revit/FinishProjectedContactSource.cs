using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Дополняет контакты по контуру помещения на высоте самой отделки.</summary>
internal sealed class FinishProjectedContactSource
{
    private const double Tolerance = FinishCandidateSearchRules.HorizontalToleranceInternal;

    public void AddContacts(FinishRoomCandidateSnapshot room, FinishRoomGeometryData roomGeometry,
        IEnumerable<FinishClassifiedElement> candidates, FinishRoomSearchBounds searchBounds,
        FinishElementGeometryCache cache, FinishOccurrenceAccumulator accumulator, List<FinishGeometryWarning> warnings)
    {
        foreach (FinishClassifiedElement candidate in candidates)
        {
            try
            {
                AxisAlignedBox3D? elementBounds = candidate.Element.Bounds;
                if (elementBounds is null) continue;
                FinishElementGeometryData? geometry = cache.Get(candidate.Element.ElementId).Geometry;
                if (geometry is null) continue;
                AxisAlignedBox3D search = searchBounds.Create(room, candidate.Category);
                double bottom = Math.Max(search.MinZ, elementBounds.MinZ - Tolerance);
                double top = Math.Min(search.MaxZ, elementBounds.MaxZ + Tolerance);
                if (top - bottom <= 1e-6) continue;
                IReadOnlyList<Solid> columns = CreateColumns(roomGeometry, bottom, top);
                if (columns.Count == 0) continue;
                double contact = 0;
                foreach (Solid solid in geometry.Solids)
                {
                    if (candidate.Category == FinishPreviewCategory.Walls)
                    {
                        // A reveal can be entirely above the room volume or lie inside its plan contour.
                        // Probe the actual finish faces at their own height, preserving openings in those faces.
                        double largestFaceContact = 0;
                        foreach (PlanarFace face in solid.Faces.OfType<PlanarFace>()
                                     .Where(face => Math.Abs(face.FaceNormal.Z) < 0.1))
                        {
                            foreach (XYZ direction in new[] { face.FaceNormal, face.FaceNormal.Negate() })
                            {
                                Solid? probe = Extrude(face.GetEdgesAsCurveLoops(), direction, Tolerance);
                                if (probe is null) continue;
                                double area = columns.Sum(column => IntersectionVolume(column, probe)) / Tolerance;
                                largestFaceContact = Math.Max(largestFaceContact, area);
                            }
                        }
                        contact += largestFaceContact;
                    }
                    else
                    {
                        foreach (Solid column in columns)
                        {
                            Solid? intersection = Intersect(column, solid);
                            if (intersection is null || intersection.Volume <= 1e-10) continue;
                            // The overlap is only an ownership score; native area is read separately.
                            contact += intersection.Faces.OfType<PlanarFace>()
                                .Where(face => face.FaceNormal.Z < -0.01)
                                .Sum(face => face.Area);
                        }
                    }
                }
                if (contact <= 1e-8 && candidate.Category != FinishPreviewCategory.Walls)
                    contact = SampleSlabContact(roomGeometry, geometry.Solids);
                double squareMeters = RevitAreaUnits.ToSquareMeters(contact);
                if (squareMeters > 1e-8)
                    accumulator.KeepLargest(new FinishOccurrence(room.ElementId, candidate.Element.ElementId,
                        candidate.Category, squareMeters, FinishQuantityMethod.ProjectedRoomFootprint));
            }
            catch (Exception exception)
            {
                warnings.Add(new FinishGeometryWarning(FinishGeometryWarningCode.BooleanIntersectionFailed,
                    $"Дополнительная проверка контура: {exception.Message}", room.ElementId,
                    candidate.Element.ElementId, candidate.Category));
            }
        }
    }

    private static double SampleSlabContact(FinishRoomGeometryData room, IReadOnlyList<Solid> solids)
    {
        // Complex slab contours can defeat Revit's Boolean kernel even with visible overlap.
        // Sample their real faces in the room's plan; this score never becomes a reported area.
        BoundingBoxXYZ bounds = room.Room.get_BoundingBox(null);
        double elevation = bounds.Min.Z + Math.Min(3.0, (bounds.Max.Z - bounds.Min.Z) / 2);
        double contact = 0;
        foreach (Face face in solids.SelectMany(solid => solid.Faces.Cast<Face>()))
        {
            if (face is PlanarFace plane && plane.FaceNormal.Z >= -0.01) continue;
            Mesh mesh = face.Triangulate();
            for (int index = 0; index < mesh.NumTriangles; index++)
            {
                MeshTriangle triangle = mesh.get_Triangle(index);
                XYZ a = triangle.get_Vertex(0), b = triangle.get_Vertex(1), c = triangle.get_Vertex(2);
                if ((b - a).CrossProduct(c - a).Z >= -1e-8) continue;
                contact += SampleTriangle(room.Room, a, b, c, elevation, 0);
            }
        }
        return contact;
    }

    private static double SampleTriangle(Autodesk.Revit.DB.Architecture.Room room,
        XYZ a, XYZ b, XYZ c, double elevation, int depth)
    {
        double area = (b - a).CrossProduct(c - a).GetLength() / 2;
        if (area > 2.5 && depth < 7)
        {
            XYZ ab = (a + b) / 2, bc = (b + c) / 2, ca = (c + a) / 2;
            return SampleTriangle(room, a, ab, ca, elevation, depth + 1)
                + SampleTriangle(room, ab, b, bc, elevation, depth + 1)
                + SampleTriangle(room, ca, bc, c, elevation, depth + 1)
                + SampleTriangle(room, ab, bc, ca, elevation, depth + 1);
        }
        XYZ center = (a + b + c) / 3;
        return room.IsPointInRoom(new XYZ(center.X, center.Y, elevation)) ? area : 0;
    }

    private static IReadOnlyList<Solid> CreateColumns(FinishRoomGeometryData room, double bottom, double top)
    {
        List<Solid> columns = [];
        // Use the computation-plane contour: the lowest face can cover only a stair landing
        // or a small part of a room with several floor elevations.
        using SpatialElementBoundaryOptions options = new()
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
        };
        IList<IList<BoundarySegment>>? boundaries = room.Room.GetBoundarySegments(options);
        if (boundaries is not null)
        {
            List<CurveLoop> contours = [];
            foreach (IList<BoundarySegment> boundary in boundaries)
            {
                CurveLoop loop = new();
                foreach (BoundarySegment segment in boundary)
                {
                    Curve curve = segment.GetCurve();
                    loop.Append(curve.CreateTransformed(Transform.CreateTranslation(
                        new XYZ(0, 0, bottom - curve.GetEndPoint(0).Z))));
                }
                contours.Add(loop);
            }
            Solid? footprint = Extrude(contours, XYZ.BasisZ, top - bottom);
            if (footprint is not null) return [footprint];
        }

        foreach (PlanarFace face in room.Solid.Faces.OfType<PlanarFace>()
                     .Where(face => face.FaceNormal.Z < -0.999))
        {
            Transform shift = Transform.CreateTranslation(new XYZ(0, 0, bottom - face.Origin.Z));
            CurveLoop[] loops = face.GetEdgesAsCurveLoops()
                .Select(loop => CurveLoop.CreateViaTransform(loop, shift)).ToArray();
            Solid? column = Extrude(loops, XYZ.BasisZ, top - bottom);
            if (column is not null) columns.Add(column);
        }
        return columns;
    }

    private static Solid? Extrude(IList<CurveLoop> loops, XYZ direction, double distance)
    {
        try { return GeometryCreationUtilities.CreateExtrusionGeometry(loops, direction, distance); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { return null; }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return null; }
    }

    private static double IntersectionVolume(Solid first, Solid second) => Intersect(first, second)?.Volume ?? 0;

    private static Solid? Intersect(Solid first, Solid second)
    {
        try { return BooleanOperationsUtils.ExecuteBooleanOperation(first, second, BooleanOperationsType.Intersect); }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            try { return BooleanOperationsUtils.ExecuteBooleanOperation(second, first, BooleanOperationsType.Intersect); }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return null; }
        }
    }
}
