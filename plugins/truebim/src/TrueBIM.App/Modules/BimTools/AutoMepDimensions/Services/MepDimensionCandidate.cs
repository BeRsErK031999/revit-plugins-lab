using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;

public sealed class MepDimensionCandidate
{
    public MepDimensionCandidate(
        string candidateId,
        string categoryName,
        string directionName,
        bool isHorizontal,
        IReadOnlyList<long> elementIds,
        int elementCount,
        int readyReferenceCount,
        int missingReferenceCount,
        XYZ dimensionStart,
        XYZ dimensionEnd,
        string dimensionLinePlacement,
        double dimensionOffsetMm,
        string message)
    {
        CandidateId = candidateId;
        CategoryName = categoryName;
        DirectionName = directionName;
        IsHorizontal = isHorizontal;
        ElementIds = elementIds;
        ElementCount = elementCount;
        ReadyReferenceCount = readyReferenceCount;
        MissingReferenceCount = missingReferenceCount;
        DimensionStart = dimensionStart;
        DimensionEnd = dimensionEnd;
        DimensionLinePlacement = MepDimensionLinePlacements.NormalizeKey(dimensionLinePlacement);
        DimensionOffsetMm = MepDimensionLinePlacements.NormalizeOffsetMillimeters(dimensionOffsetMm);
        Message = message;
    }

    public string CandidateId { get; }

    public string CategoryName { get; }

    public string DirectionName { get; }

    public bool IsHorizontal { get; }

    public IReadOnlyList<long> ElementIds { get; }

    public int ElementCount { get; }

    public int ReadyReferenceCount { get; }

    public int MissingReferenceCount { get; }

    public XYZ DimensionStart { get; }

    public XYZ DimensionEnd { get; }

    public string DimensionLinePlacement { get; }

    public double DimensionOffsetMm { get; }

    public string Message { get; }

    public bool CanApply => ReadyReferenceCount >= 2;

    public string DimensionLineDisplay
    {
        get
        {
            string orientation = IsHorizontal ? "Вертикальная размерная линия" : "Горизонтальная размерная линия";
            string placement = MepDimensionLinePlacements.FormatForDisplay(DimensionLinePlacement, DimensionOffsetMm);
            return $"{orientation}; {placement}";
        }
    }

    public MepDimensionCandidateRow ToRow()
    {
        string status = CanApply ? MepDimensionStatuses.Ready : MepDimensionStatuses.Skipped;
        return new MepDimensionCandidateRow(
            CandidateId,
            CategoryName,
            DirectionName,
            ElementCount,
            ReadyReferenceCount,
            MissingReferenceCount,
            DimensionLineDisplay,
            status,
            Message,
            CanApply);
    }
}
