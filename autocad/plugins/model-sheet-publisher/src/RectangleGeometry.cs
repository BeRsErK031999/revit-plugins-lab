using System;
using System.Linq;
using System.Collections.Generic;
namespace ModelSheetPublisher {
    public sealed class Segment
    {
        public double Axis, Start, End;
        public Segment(double axis, double a, double b) { Axis = axis; Start = Math.Min(a, b); End = Math.Max(a, b); }
    }

    public static class RectangleGeometry
    {
        public const double Tolerance = .05;
        public static List<Segment> Merge(IEnumerable<Segment> segments)
        {
            var result = new List<Segment>();
            foreach (var group in segments.GroupBy(s => Math.Round(s.Axis / Tolerance)))
            {
                Segment current = null;
                foreach (var s in group.OrderBy(s => s.Start))
                {
                    if (current == null || s.Start > current.End + Tolerance) { current = new Segment(s.Axis, s.Start, s.End); result.Add(current); }
                    else current.End = Math.Max(current.End, s.End);
                }
            }
            return result;
        }
        public static IEnumerable<Box> FromLines(List<Segment> horizontal, List<Segment> vertical)
        {
            var hs = Merge(horizontal);
            var vs = Merge(vertical);
            foreach (var h in hs)
            {
                var lefts = vs.Where(v => Math.Abs(v.Axis - h.Start) <= Tolerance && v.Start <= h.Axis + Tolerance && v.End > h.Axis + Tolerance).ToList();
                var rights = vs.Where(v => Math.Abs(v.Axis - h.End) <= Tolerance && v.Start <= h.Axis + Tolerance && v.End > h.Axis + Tolerance).ToList();
                foreach (var left in lefts)
                foreach (var right in rights)
                {
                    double maxY = Math.Min(left.End, right.End);
                    foreach (var top in hs.Where(t => t.Axis > h.Axis + Tolerance && t.Axis <= maxY + Tolerance && t.Start <= h.Start + Tolerance && t.End >= h.End - Tolerance))
                        yield return new Box(h.Start, h.Axis, h.End - h.Start, top.Axis - h.Axis);
                }
            }
        }
    }

}
