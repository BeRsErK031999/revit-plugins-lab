#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed record SlabEndCheck(Rebar Bar, List<XYZ[]> Positions)
{
    private const double Tolerance = 0.15 * SlabGeometry.Mm;

    private IList<Curve> Lines(int index) =>
        Bar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, index);

    internal bool Matches()
    {
        if (Bar.Quantity != Positions.Count) return false;
        for (int i = 0; i < Positions.Count; i++)
        {
            var lines = Lines(i);
            if (lines.Count != 3) return false;
            var vertices = lines.SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).ToList();
            if (Positions[i].Any(p => !vertices.Any(v => p.DistanceTo(v) <= Tolerance))) return false;
        }
        return true;
    }

    internal void RestoreDimensions(RebarShape shape)
    {
        var actual = Lines(0);
        var expected = Positions[0];
        var definition = (RebarShapeDefinitionBySegments)shape.GetRebarShapeDefinition();
        var changed = new HashSet<ElementId>();
        for (int i = 0; i < 3; i++)
        {
            double difference = expected[i].DistanceTo(expected[i + 1]) - actual[i].Length;
            if (Math.Abs(difference) <= Tolerance) continue;
            var id = definition.GetSegment(i).GetConstraints().OfType<RebarShapeConstraintSegmentLength>()
                .Select(c => c.GetParamId()).FirstOrDefault();
            if (id == null || !changed.Add(id)) continue;
            var parameter = Bar.Parameters.Cast<Parameter>().FirstOrDefault(p => p.Id == id);
            if (parameter != null && !parameter.IsReadOnly) parameter.Set(parameter.AsDouble() + difference);
        }
    }

    internal void RestorePosition()
    {
        XYZ delta = (Positions[0][1] + Positions[0][2]) / 2 - Lines(0)[1].Evaluate(0.5, true);
        if (delta.GetLength() > Tolerance) ElementTransformUtils.MoveElement(Bar.Document, Bar.Id, delta);
    }
}
#endif
