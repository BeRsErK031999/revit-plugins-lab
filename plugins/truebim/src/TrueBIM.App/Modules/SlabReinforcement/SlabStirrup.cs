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

    private static IList<Curve> Centerlines(Rebar bar) => bar.GetTransformedCenterlineCurves(
        false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0);

    internal void RestoreDimensions(SlabGeometry g, Rebar bar)
    {
        var definition = ((RebarShape)bar.Document.GetElement(bar.GetShapeId())).GetRebarShapeDefinition() as RebarShapeDefinitionBySegments;
        var sides = bar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
        if (definition == null || definition.NumberOfSegments != sides.Count) return;
        var curves = Centerlines(bar);
        if (curves.Count == 0) return;
        // ScaleToBox can snap the web back to the same face. Update the driving dimensions
        // of the committed instance using the measured outside envelope, including bends.
        var width = g.Range(Axis, curves);
        var height = g.Range(Up, curves);
        var changed = new HashSet<ElementId>();
        for (int i = 0; i < sides.Count; i++)
        {
            if (sides[i] is not Line side) continue;
            double difference;
            if (Math.Abs(side.Direction.DotProduct(Axis)) > 0.99999)
                difference = Length - Diameter - (width.Max - width.Min);
            else if (Math.Abs(side.Direction.DotProduct(Up)) > 0.99999)
                difference = Height - Diameter - (height.Max - height.Min);
            else continue;
            if (Math.Abs(difference) <= Tolerance) continue;
            var id = definition.GetSegment(i).GetConstraints().OfType<RebarShapeConstraintSegmentLength>()
                .Select(c => c.GetParamId()).FirstOrDefault();
            if (id == null || !changed.Add(id)) continue;
            var parameter = bar.Parameters.Cast<Parameter>().FirstOrDefault(p => p.Id == id);
            if (parameter != null && !parameter.IsReadOnly) parameter.Set(parameter.AsDouble() + difference);
        }
    }

    internal void RestorePosition(SlabGeometry g, Rebar bar)
    {
        var curves = Centerlines(bar);
        if (curves.Count == 0) return;
        double Offset(XYZ axis, double expectedMin) => g.Coord(Origin, axis) + expectedMin - g.Range(axis, curves).Min;
        XYZ cross = Up.CrossProduct(Axis);
        XYZ delta = Axis * Offset(Axis, Diameter / 2) + Up * Offset(Up, Diameter / 2) + cross * Offset(cross, 0);
        if (delta.GetLength() > Tolerance) ElementTransformUtils.MoveElement(bar.Document, bar.Id, delta);
    }

    internal string? Mismatch(SlabGeometry g, Rebar bar)
    {
        var curves = Centerlines(bar);
        if (curves.Count == 0) return "Revit не вернул осевые линии хомута.";
        string? Deviation(XYZ axis, double min, double max, string dimension)
        {
            var range = g.Range(axis, curves);
            double origin = g.Coord(Origin, axis);
            double first = range.Min - origin - min, last = range.Max - origin - max;
            if (Math.Abs(first) <= Tolerance && Math.Abs(last) <= Tolerance) return null;
            return $"{dimension}. Отклонения крайних точек от расчётного положения: {first / Mm:F2} и {last / Mm:F2} мм.";
        }
        double radius = Diameter / 2;
        return Deviation(Axis, radius, Length - radius, $"Длина по проекту {Length / Mm:F2} мм")
            ?? Deviation(Up, radius, Height - radius, $"Высота по проекту {Height / Mm:F2} мм")
            ?? Deviation(Up.CrossProduct(Axis), 0, 0, "Поперечное положение ряда");
    }
}
#endif
