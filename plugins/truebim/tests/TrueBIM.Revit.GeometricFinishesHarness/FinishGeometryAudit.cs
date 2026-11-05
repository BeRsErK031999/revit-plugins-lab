using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.GeometricFinishes;

namespace TrueBIM.Revit.GeometricFinishesHarness;
internal static class FinishGeometryAudit
{
    public static void Run(Document doc, string root)
    {
        var gaps = new List<object>();
        foreach (Room room in new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).OfType<Room>())
        {
            var loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
            if (loops == null) continue;
            foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                Curve a = loop[i].GetCurve(), b = loop[(i + 1) % loop.Count].GetCurve();
                double distance = a.GetEndPoint(1).DistanceTo(b.GetEndPoint(0));
                if (distance > 1e-7) gaps.Add(new { Room = room.Number, GapMm = distance * 304.8, A = a.GetType().Name,
                    B = b.GetType().Name, LengthA = a.Length * 304.8, LengthB = b.Length * 304.8,
                    End = V(a.GetEndPoint(1)), Start = V(b.GetEndPoint(0)) });
            }
        }
        var inserts = new List<object>();
        foreach (FamilyInstance insert in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Host is Wall && f.SuperComponent == null && new long[] { 955791, 965471, 963997, 966592, 968115 }.Contains(f.Id.Value)))
        {
            Wall wall = (Wall)insert.Host;
            var faces = new List<object>();
            var raw = wall.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine });
            foreach (Solid solid in raw.OfType<Solid>())
            foreach (Face face in solid.Faces)
            {
                var edgeInfo = new List<object>();
                foreach (EdgeArray loop in face.EdgeLoops)
                foreach (Edge edge in loop)
                {
                    var ids = wall.GetGeneratingElementIds(edge).Select(id => id.Value).ToArray();
                    if (ids.Contains(insert.Id.Value)) edgeInfo.Add(new { Ids = ids, Start = V(edge.AsCurve().GetEndPoint(0)), End = V(edge.AsCurve().GetEndPoint(1)) });
                }
                var faceIds = wall.GetGeneratingElementIds(face).Select(id => id.Value).ToArray();
                if (edgeInfo.Count > 0 || faceIds.Contains(insert.Id.Value)) faces.Add(new {
                    Generators = faceIds, Normal = face is PlanarFace plane ? V(plane.FaceNormal) : null,
                    Edges = edgeInfo,
                    Bounds = face.GetEdgesAsCurveLoops().SelectMany(l => l).SelectMany(c => c.Tessellate()).Select(V).ToArray() });
            }
            string? error = null; int ifcEdges = 0;
            try { ifcEdges = FinishOpenings.InstanceCutout(wall, insert).Count; } catch (Exception ex) { error = ex.Message; }
            inserts.Add(new { Id = insert.Id.Value, insert.Name, Wall = wall.Id.Value, IfcEdges = ifcEdges, Error = error, Faces = faces,
                Axis = new[] { V(((LocationCurve)wall.Location).Curve.GetEndPoint(0)), V(((LocationCurve)wall.Location).Curve.GetEndPoint(1)) },
                Box = new[] { V(wall.get_BoundingBox(null).Min), V(wall.get_BoundingBox(null).Max) },
                InstanceBox = new[] { V(insert.get_BoundingBox(null).Min), V(insert.get_BoundingBox(null).Max) },
                Origin = insert.Location is LocationPoint lp ? V(lp.Point) : null,
                Dimensions = insert.Symbol.Parameters.Cast<Parameter>().Where(p => p.Definition.Name.Contains("Ширин") || p.Definition.Name.Contains("Высот"))
                    .Select(p => new { p.Definition.Name, Value = p.AsValueString() }).ToArray()
            });
        }
        File.WriteAllText(Path.Combine(root, "geometry-audit.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new { Gaps = gaps, Inserts = inserts }));
    }
    private static double[] V(XYZ p) => new[] { p.X, p.Y, p.Z };
}
