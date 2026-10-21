using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Modules.RebarSplit.Revit;

namespace TrueBIM.Revit.RebarSplitHarness;

internal sealed class RebarSplitDimensionAudit
{
    private readonly ElementId originalId;
    private readonly ElementId viewId;
    private readonly ElementId typeId;
    private readonly Line line;
    private readonly List<SegmentState> segments;
    private readonly List<(ElementId Id, IList<Curve>? BarCurves)> references;
    private readonly int? equalityDisplay;
    private readonly int? leader;
    private readonly bool equalSegments;

    private RebarSplitDimensionAudit(Document document, Dimension dimension, IReadOnlyList<ElementId> candidates)
    {
        originalId = dimension.Id;
        viewId = dimension.OwnerViewId;
        typeId = dimension.GetTypeId();
        line = (Line)dimension.Curve.Clone();
        segments = Segments(dimension);
        equalityDisplay = dimension.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ)?.AsInteger();
        leader = dimension.get_Parameter(BuiltInParameter.DIM_LEADER)?.AsInteger();
        equalSegments = dimension.AreSegmentsEqual;
        references = dimension.References.Cast<Reference>().Select(reference =>
        {
            Rebar? source = document.GetElement(reference.ElementId) as Rebar;
            int position = source is not null ? source.GetBarIndexFromReference(reference) : -1;
            IList<Curve>? curves = source is not null && candidates.Contains(source.Id) && position >= 0
                ? RebarSplitGeometry.Curves(source, position).Select(curve => curve.Clone()).ToList()
                : null;
            return (reference.ElementId, curves);
        }).ToList();
    }

    public static List<RebarSplitDimensionAudit> Capture(Document document, IReadOnlyList<ElementId> candidates)
    {
        HashSet<ElementId> multiDimensions = new(new FilteredElementCollector(document)
            .OfClass(typeof(MultiReferenceAnnotation)).Cast<MultiReferenceAnnotation>().Select(item => item.DimensionId));
        return new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(dimension => !multiDimensions.Contains(dimension.Id) && dimension.Curve is Line &&
                dimension.References.Cast<Reference>().Any(reference => candidates.Contains(reference.ElementId)))
            .Select(dimension => new RebarSplitDimensionAudit(document, dimension, candidates)).ToList();
    }

    public DimensionAuditResult Verify(Document document, IReadOnlyList<ElementId> created, bool requireAvailableReferences)
    {
        List<Dimension> matches = new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(dimension => dimension.Id != originalId && dimension.OwnerViewId == viewId && dimension.GetTypeId() == typeId &&
                dimension.Curve is Line candidate && candidate.Origin.DistanceTo(line.Origin) < 1e-7 &&
                candidate.Direction.IsAlmostEqualTo(line.Direction)).ToList();
        if (matches.Count != 1)
            return new(originalId.IntegerValue, null, false, $"Expected one replacement dimension; found {matches.Count}.");
        Dimension target = matches[0];
        List<SegmentState> actualSegments = Segments(target);
        List<Reference> actualReferences = target.References.Cast<Reference>().ToList();
        bool valuesAndPresentation = segments.Count == actualSegments.Count &&
            segments.Zip(actualSegments, (before, after) => before.Matches(after)).All(equal => equal) &&
            equalityDisplay == target.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ)?.AsInteger() &&
            leader == target.get_Parameter(BuiltInParameter.DIM_LEADER)?.AsInteger() &&
            equalSegments == target.AreSegmentsEqual;
        bool mappedBars = actualReferences.Count == references.Count;
        for (int index = 0; mappedBars && index < references.Count; index++)
        {
            var expected = references[index];
            ElementId actualId = actualReferences[index].ElementId;
            if (expected.BarCurves is null) mappedBars &= actualId == expected.Id;
            else mappedBars &= created.Contains(actualId) && document.GetElement(actualId) is Rebar bar &&
                RebarSplitGeometry.SameCurves(expected.BarCurves, RebarSplitGeometry.Curves(bar, 0));
        }
        bool passed = document.GetElement(originalId) is null && (!requireAvailableReferences || target.AreReferencesAvailable) &&
            valuesAndPresentation && mappedBars;
        return new(originalId.IntegerValue, target.Id.IntegerValue, passed,
            $"ReferencesAvailable={target.AreReferencesAvailable}; valuesAndPresentation={valuesAndPresentation}; correctBars={mappedBars}; " +
            $"segments=[{string.Join("; ", segments.Zip(actualSegments, (before, after) => before.Difference(after)))}]");
    }

    private static List<SegmentState> Segments(Dimension dimension) => dimension.NumberOfSegments == 0
        ? new List<SegmentState> { new(dimension.Value, dimension.IsTextPositionAdjustable() ? dimension.TextPosition : null, dimension.ValueOverride,
            dimension.Above, dimension.Below, dimension.Prefix, dimension.Suffix) }
        : dimension.Segments.Cast<DimensionSegment>().Select(segment => new SegmentState(segment.Value,
            segment.IsTextPositionAdjustable() ? segment.TextPosition : null,
            segment.ValueOverride, segment.Above, segment.Below, segment.Prefix, segment.Suffix)).ToList();

    private sealed record SegmentState(double? Value, XYZ? TextPosition, string ValueOverride,
        string Above, string Below, string Prefix, string Suffix)
    {
        public bool Matches(SegmentState other) => Value.HasValue && other.Value.HasValue &&
            Math.Abs(Value.Value - other.Value.Value) <= RebarSplitPolicy.LengthToleranceFeet &&
            (TextPosition is null && other.TextPosition is null ||
                TextPosition is not null && other.TextPosition is not null && TextPosition.DistanceTo(other.TextPosition) < 1e-7) &&
            SameText(ValueOverride, other.ValueOverride) && SameText(Above, other.Above) && SameText(Below, other.Below) &&
            SameText(Prefix, other.Prefix) && SameText(Suffix, other.Suffix);

        public string Difference(SegmentState other) =>
            $"value={Value}/{other.Value}; text={TextPosition}/{other.TextPosition}; " +
            $"override={ValueOverride}/{other.ValueOverride}; above={Above}/{other.Above}; below={Below}/{other.Below}; " +
            $"prefix={Prefix}/{other.Prefix}; suffix={Suffix}/{other.Suffix}";

        private static bool SameText(string? first, string? second) => (first ?? "") == (second ?? "");
    }
}

internal sealed record DimensionAuditResult(int SourceId, int? TargetId, bool Passed, string Details);
