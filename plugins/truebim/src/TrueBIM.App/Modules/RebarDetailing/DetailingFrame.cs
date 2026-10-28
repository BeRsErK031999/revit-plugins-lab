using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

internal sealed class DetailingFrame
{
    public View View { get; }
    public XYZ Right => View.RightDirection;
    public XYZ Up => View.UpDirection;
    public XYZ Normal => View.ViewDirection;
    public XYZ Origin => View.Origin;
    public double Paper(double mm) => mm * View.Scale / 304.8;
    public bool Vertical => Math.Abs(Normal.Z) < 0.1;
    private readonly BoundingBoxXYZ crop;
    private readonly Transform inverse;

    public DetailingFrame(View view)
    {
        View = view; crop = view.CropBox; inverse = crop.Transform.Inverse;
        if (view is ViewPlan plan && Math.Abs(crop.Transform.BasisZ.Z) > 0.999)
        {
            using PlanViewRange range = plan.GetViewRange();
            double? bottom = Height(PlanViewPlane.BottomClipPlane), depth = Height(PlanViewPlane.ViewDepthPlane), top = Height(PlanViewPlane.TopClipPlane);
            double low = Math.Min(bottom ?? double.PositiveInfinity, depth ?? double.PositiveInfinity);
            if (!double.IsInfinity(low)) crop.Min = new XYZ(crop.Min.X, crop.Min.Y, (low - crop.Transform.Origin.Z) / crop.Transform.BasisZ.Z);
            if (top.HasValue) crop.Max = new XYZ(crop.Max.X, crop.Max.Y, (top.Value - crop.Transform.Origin.Z) / crop.Transform.BasisZ.Z);
            double? Height(PlanViewPlane plane)
            {
                ElementId levelId = range.GetLevelId(plane);
                if (levelId == PlanViewRange.Unlimited) return null;
                Level? level = levelId == PlanViewRange.Current ? plan.GenLevel : view.Document.GetElement(levelId) as Level;
                return level == null ? null : level.ProjectElevation + range.GetOffset(plane);
            }
        }
    }
    public double X(XYZ p) => (p - Origin).DotProduct(Right);
    public double Y(XYZ p) => (p - Origin).DotProduct(Up);
    public double Z(XYZ p) => (p - Origin).DotProduct(Normal);
    public XYZ Point(double x, double y) => Origin + Right * x + Up * y;
    public XYZ Flatten(XYZ p) => Point(X(p), Y(p));

    public bool Contains(XYZ p, double tolerance = 0.002)
    {
        if (!View.CropBoxActive) return true;
        XYZ q = inverse.OfPoint(p);
        return q.X >= crop.Min.X - tolerance && q.X <= crop.Max.X + tolerance
            && q.Y >= crop.Min.Y - tolerance && q.Y <= crop.Max.Y + tolerance
            && q.Z >= crop.Min.Z - tolerance && q.Z <= crop.Max.Z + tolerance;
    }

    // Clip in all three view coordinates: a bar whose bounding box crosses the view
    // is not necessarily present inside the section itself.
    public XYZ? Clip(XYZ a, XYZ b)
    {
        return ClipSegment(a, b, out XYZ start, out XYZ end) ? (start + end) / 2 : null;
    }

    public bool ClipSegment(XYZ a, XYZ b, out XYZ startPoint, out XYZ endPoint)
    {
        startPoint = a; endPoint = b;
        if (!View.CropBoxActive) return true;
        XYZ p = inverse.OfPoint(a), q = inverse.OfPoint(b);
        double lower = 0, upper = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            double start = p[axis], delta = q[axis] - start;
            double min = crop.Min[axis] - 0.002, max = crop.Max[axis] + 0.002;
            if (Math.Abs(delta) < 1e-10) { if (start < min || start > max) return false; continue; }
            double t0 = (min - start) / delta, t1 = (max - start) / delta;
            lower = Math.Max(lower, Math.Min(t0, t1)); upper = Math.Min(upper, Math.Max(t0, t1));
            if (lower > upper) return false;
        }
        startPoint = a + (b - a) * lower; endPoint = a + (b - a) * upper;
        return true;
    }

    public bool Intersects(BoundingBoxXYZ? box, Transform placement)
    {
        if (box == null) return false;
        if (!View.CropBoxActive) return true;
        List<XYZ> points = new();
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            points.Add(inverse.OfPoint(placement.OfPoint(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)))));
        return Enumerable.Range(0, 3).All(axis => points.Max(p => p[axis]) >= crop.Min[axis] - 0.002
            && points.Min(p => p[axis]) <= crop.Max[axis] + 0.002);
    }
}
