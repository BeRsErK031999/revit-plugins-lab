#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed record SlabStirrup(XYZ Origin, XYZ Axis, XYZ Up, double Length, double Height, double Diameter)
{
    internal Rebar Place(Floor host, RebarShape shape, RebarBarType type)
    {
        var bar = Rebar.CreateFromRebarShape(host.Document, shape, type, host, Origin, Axis, Up);
        if (bar == null) throw new InvalidOperationException("Revit не принял форму Х1 на коротком участке.");
        bar.GetShapeDrivenAccessor().ScaleToBox(Origin, Axis * Length, Up * Height);
        return bar;
    }

    internal bool Matches(SlabGeometry g, Rebar bar)
    {
        const double tolerance = 0.05 * Mm;
        var curves = bar.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
        if (curves.Count == 0) return false;
        bool Fits(XYZ axis, double min, double max)
        {
            var range = g.Range(axis, curves);
            double origin = g.Coord(Origin, axis);
            return Math.Abs(range.Min - origin - min) <= tolerance && Math.Abs(range.Max - origin - max) <= tolerance;
        }
        double radius = Diameter / 2;
        return Fits(Axis, radius, Length - radius) && Fits(Up, radius, Height - radius) && Fits(Up.CrossProduct(Axis), 0, 0);
    }
}
#endif
