using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

internal static class RebarSplitGeometry
{
    public static bool IsSupportedSet(Element? element) => element is Rebar rebar
        && rebar.IsRebarShapeDriven() && rebar.LayoutRule != RebarLayoutRule.Single && rebar.Quantity > 1;

    public static List<int> ExistingPositions(Rebar source) => Enumerable.Range(0, source.NumberOfBarPositions)
        .Where(source.DoesBarExistAtPosition).ToList();

    public static IList<Curve> Curves(Rebar source, int index, bool driving = false)
    {
        MultiplanarOption option = driving ? MultiplanarOption.IncludeOnlyPlanarCurves : MultiplanarOption.IncludeAllMultiplanarCurves;
#if REVIT2022_OR_GREATER
        IList<Curve> curves = source.GetTransformedCenterlineCurves(false, driving, driving, option, index);
#else
        Transform position = source.GetShapeDrivenAccessor().GetBarPositionTransform(index);
        IList<Curve> curves = source.GetCenterlineCurves(false, driving, driving, option, index)
            .Select(curve => curve.CreateTransformed(position)).ToList();
#endif
        if (curves.Count == 0) throw new InvalidOperationException($"Набор {source.Id}: не удалось получить геометрию стержня {index + 1}.");
        return curves;
    }

    public static HashSet<ElementId> ShapeParameters(Rebar source)
    {
        RebarShape shape = (RebarShape)source.Document.GetElement(source.GetShapeId());
        HashSet<ElementId> ids = new(shape.GetRebarShapeDefinition().GetParameters());
        RebarShapeMultiplanarDefinition? multiplanar = shape.GetMultiplanarDefinition();
        if (multiplanar is not null) ids.Add(multiplanar.DepthParamId);
        return ids;
    }

    public static Rebar CreateSingle(Document document, Rebar source, int index, ISet<ElementId> shapeParameterIds)
    {
        RebarShape shape = (RebarShape)document.GetElement(source.GetShapeId());
        RebarBarType barType = (RebarBarType)document.GetElement(source.GetTypeId());
        Element host = document.GetElement(source.GetHostId())
            ?? throw new InvalidOperationException($"Набор {source.Id}: основание арматуры недоступно.");
        XYZ normal = source.GetShapeDrivenAccessor().Normal;
#if REVIT2022_OR_GREATER
        normal = source.GetMovedBarTransform(index).OfVector(normal);
#endif
#if REVIT2026_OR_GREATER
        using BarTerminationsData terminations = new(document)
        {
            TerminationOrientationAtStart = source.GetTerminationOrientation(0),
            TerminationOrientationAtEnd = source.GetTerminationOrientation(1),
            TerminationRotationAngleAtStart = source.GetTerminationRotationAngle(0),
            TerminationRotationAngleAtEnd = source.GetTerminationRotationAngle(1),
            EndTreatmentTypeIdAtStart = source.GetEndTreatmentTypeId(0),
            EndTreatmentTypeIdAtEnd = source.GetEndTreatmentTypeId(1)
        };
        if (source.GetCrankTypeId(0) != ElementId.InvalidElementId) terminations.CrankTypeIdAtStart = source.GetCrankTypeId(0);
        else terminations.HookTypeIdAtStart = source.GetHookTypeId(0);
        if (source.GetCrankTypeId(1) != ElementId.InvalidElementId) terminations.CrankTypeIdAtEnd = source.GetCrankTypeId(1);
        else terminations.HookTypeIdAtEnd = source.GetHookTypeId(1);
        Rebar? target = Rebar.CreateFromCurvesAndShape(document, shape, barType, host, normal, Curves(source, index, true), terminations);
#else
        Rebar? target = Rebar.CreateFromCurvesAndShape(document, shape, barType,
            document.GetElement(source.GetHookTypeId(0)) as RebarHookType,
            document.GetElement(source.GetHookTypeId(1)) as RebarHookType,
            host, normal, Curves(source, index, true), source.GetHookOrientation(0), source.GetHookOrientation(1)
#if REVIT2021_OR_GREATER
            , source.GetHookRotationAngle(0), source.GetHookRotationAngle(1),
            source.GetEndTreatmentTypeId(0), source.GetEndTreatmentTypeId(1)
#endif
        );
#endif
        if (target is null) throw new InvalidOperationException($"Набор {source.Id}: Revit не создал отдельный стержень.");
        target.GetShapeDrivenAccessor().SetLayoutAsSingle();
        document.Regenerate();
        // В том числе глубина пространственной формы; значения берутся у конкретного стержня.
        foreach (ElementId id in shapeParameterIds)
        {
            Parameter? original = source.Parameters.Cast<Parameter>().FirstOrDefault(parameter => parameter.Id == id);
            if (original is null) continue;
            Parameter? parameter = target.get_Parameter(original.Definition);
            if (source.GetParameterValueAtIndex(id, index) is not DoubleParameterValue value) continue;
            if (parameter is not null && Math.Abs(parameter.AsDouble() - value.Value) <= 1e-9) continue;
            // Вычисляемые длины крюков заполняются решателем формы; их проверяет сравнение геометрии.
            if (parameter?.IsReadOnly == true) continue;
            if (parameter is null || !parameter.Set(value.Value))
                throw new InvalidOperationException($"Набор {source.Id}: не удалось сохранить размер формы «{original.Definition.Name}».");
        }
        return target;
    }

    public static void RestorePosition(Document document, Rebar target, IList<Curve> expected)
    {
        IList<Curve> actual = Curves(target, 0);
        if (SameCurves(expected, actual)) return;
        // При создании Revit может притянуть плоскость нового стержня к защитному слою.
        // Исправляем только общий перенос: форму и размеры нельзя подменять подгонкой.
        foreach (XYZ anchor in new[] { actual[0].GetEndPoint(0), actual[actual.Count - 1].GetEndPoint(1) })
        {
            XYZ offset = expected[0].GetEndPoint(0) - anchor;
            Transform translation = Transform.CreateTranslation(offset);
            if (!SameCurves(expected, actual.Select(curve => curve.CreateTransformed(translation)).ToList())) continue;
            ElementTransformUtils.MoveElement(document, target.Id, offset);
            document.Regenerate();
            return;
        }
    }

    public static void Validate(Rebar target, IList<Curve> expected, ElementId typeId, ElementId hostId)
    {
        IList<Curve> actual = Curves(target, 0);
        if (target.Quantity != 1 || target.LayoutRule != RebarLayoutRule.Single
            || target.GetTypeId() != typeId || target.GetHostId() != hostId
            || !SameCurves(expected, actual))
            throw new InvalidOperationException($"Стержень {target.Id}: форма, длина, положение, тип или основание отличаются от исходного набора. Изменения отменены.");
    }

    internal static bool SameCurves(IList<Curve> expected, IList<Curve> actual)
    {
        if (expected.Count != actual.Count) return false;
        return Match(false) || Match(true);
        bool Match(bool reverse)
        {
            for (int i = 0; i < expected.Count; i++)
            {
                Curve a = expected[i];
                Curve b = actual[reverse ? actual.Count - 1 - i : i];
                if (a.GetType() != b.GetType() || Math.Abs(a.Length - b.Length) > RebarSplitPolicy.LengthToleranceFeet) return false;
                for (int step = 0; step <= 8; step++)
                {
                    double parameter = step / 8.0;
                    if (a.Evaluate(parameter, true).DistanceTo(b.Evaluate(reverse ? 1 - parameter : parameter, true))
                        > RebarSplitPolicy.LengthToleranceFeet) return false;
                }
            }
            return true;
        }
    }
}
