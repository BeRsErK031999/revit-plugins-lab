using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ModelSheetPublisher
{
    public static class FrameFinder
    {
        public static List<Box> Read(Database db)
        {
            var boxes = new List<Box>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                Collect((BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead), tr, Matrix3d.Identity, boxes, 0);
                tr.Commit();
            }
            return boxes.Where(b => b.Width > .1 && b.Height > .1).GroupBy(b => b.ToString()).Select(g => g.First()).ToList();
        }

        public static Box FromEntity(Database db, ObjectId id, Point3d point, List<Box> drawingFrames)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity is BlockReference block)
                {
                    var frames = new List<Box>();
                    Collect((BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead), tr, block.BlockTransform, frames, 0);
                    // A frame block may include text outside its border. Use its actual rectangles, not block extents.
                    return Pick(frames, point.X, point.Y, .1) ?? frames.OrderByDescending(b => b.Width * b.Height).FirstOrDefault();
                }
                if (entity is Polyline polyline && polyline.Closed && polyline.NumberOfVertices >= 4)
                {
                    var vertices = Enumerable.Range(0, polyline.NumberOfVertices).Select(polyline.GetPoint3dAt).ToList();
                    double x = vertices.Min(v => v.X), y = vertices.Min(v => v.Y);
                    var box = new Box(x, y, vertices.Max(v => v.X) - x, vertices.Max(v => v.Y) - y);
                    bool rectangle = box.Width > .1 && box.Height > .1;
                    for (int i = 0; i < polyline.NumberOfVertices && rectangle; i++)
                    {
                        var a = polyline.GetPoint3dAt(i); var b = polyline.GetPoint3dAt((i + 1) % polyline.NumberOfVertices);
                        rectangle = Math.Abs(polyline.GetBulgeAt(i)) < 1e-8 && OnBorder(box, a, b);
                    }
                    if (rectangle) return box;
                }
                var edges = new List<Tuple<Point3d, Point3d>>();
                if (entity is Line line) edges.Add(Tuple.Create(line.StartPoint, line.EndPoint));
                if (entity is Polyline p)
                    for (int i = 0; i < p.NumberOfVertices - (p.Closed ? 0 : 1); i++)
                        if (Math.Abs(p.GetBulgeAt(i)) < 1e-8) edges.Add(Tuple.Create(p.GetPoint3dAt(i), p.GetPoint3dAt((i + 1) % p.NumberOfVertices)));
                // For exploded frames only accept rectangles whose boundary includes the selected segment.
                return drawingFrames.Where(b => edges.Any(e => OnBorder(b, e.Item1, e.Item2)))
                    .OrderByDescending(b => b.Width * b.Height).FirstOrDefault();
            }
        }

        private static bool OnBorder(Box box, Point3d a, Point3d b)
        {
            const double t = RectangleGeometry.Tolerance;
            if (!box.Contains(a.X, a.Y, t) || !box.Contains(b.X, b.Y, t)) return false;
            bool horizontal = Math.Abs(a.Y - b.Y) < t && (Math.Abs(a.Y - box.Y) < t || Math.Abs(a.Y - box.Top) < t);
            bool vertical = Math.Abs(a.X - b.X) < t && (Math.Abs(a.X - box.X) < t || Math.Abs(a.X - box.Right) < t);
            return (horizontal || vertical) && a.DistanceTo(b) > t;
        }

        private static void Collect(BlockTableRecord record, Transaction tr, Matrix3d transform, List<Box> boxes, int depth)
        {
            if (depth > 12 || record.IsFromExternalReference) return;
            var hs = new List<Segment>(); var vs = new List<Segment>();
            Action<Point3d, Point3d> add = (a, b) =>
            {
                a = a.TransformBy(transform); b = b.TransformBy(transform);
                if (Math.Abs(a.Z - b.Z) > .05) return;
                if (Math.Abs(a.Y - b.Y) < .05 && Math.Abs(a.X - b.X) > .05) hs.Add(new Segment(a.Y, a.X, b.X));
                if (Math.Abs(a.X - b.X) < .05 && Math.Abs(a.Y - b.Y) > .05) vs.Add(new Segment(a.X, a.Y, b.Y));
            };
            foreach (ObjectId id in record)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity == null || !entity.Visible) continue;
                var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                if (layer.IsOff || layer.IsFrozen) continue;
                if (entity is Line line) add(line.StartPoint, line.EndPoint);
                else if (entity is Polyline p)
                {
                    int n = p.NumberOfVertices;
                    bool rectangle = p.Closed && n == 4;
                    var points = new List<Point3d>();
                    for (int i = 0; i < n; i++)
                    {
                        var a = p.GetPoint3dAt(i).TransformBy(transform); points.Add(a);
                        if (i == n - 1 && !p.Closed) break;
                        var b = p.GetPoint3dAt((i + 1) % n).TransformBy(transform);
                        if (Math.Abs(p.GetBulgeAt(i)) > 1e-8) { rectangle = false; continue; }
                        add(p.GetPoint3dAt(i), p.GetPoint3dAt((i + 1) % n));
                        if (Math.Abs(a.X - b.X) > .05 && Math.Abs(a.Y - b.Y) > .05) rectangle = false;
                    }
                    if (rectangle)
                    {
                        double x = points.Min(a => a.X), y = points.Min(a => a.Y);
                        boxes.Add(new Box(x, y, points.Max(a => a.X) - x, points.Max(a => a.Y) - y));
                    }
                }
                else if (entity is BlockReference block)
                    Collect((BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead), tr, transform * block.BlockTransform, boxes, depth + 1);
            }
            boxes.AddRange(RectangleGeometry.FromLines(hs, vs));
        }

        public static Box Pick(List<Box> boxes, double x, double y, double pickTolerance)
        {
            // A click inside a sheet must not prefer a nearby title-block cell over its outer frame.
            var enclosing = boxes.Where(b => b.Contains(x, y)).OrderByDescending(b => b.Width * b.Height).FirstOrDefault();
            if (enclosing != null) return enclosing;
            // Allow a small miss outside the border; largest shared boundary wins over title-block cells.
            var near = boxes.Where(b => b.Contains(x, y, pickTolerance)
                && Math.Min(Math.Min(Math.Abs(x - b.X), Math.Abs(x - b.Right)), Math.Min(Math.Abs(y - b.Y), Math.Abs(y - b.Top))) <= pickTolerance).ToList();
            if (near.Count != 0) return near.OrderByDescending(b => b.Width * b.Height).First();
            return null;
        }
    }
}
