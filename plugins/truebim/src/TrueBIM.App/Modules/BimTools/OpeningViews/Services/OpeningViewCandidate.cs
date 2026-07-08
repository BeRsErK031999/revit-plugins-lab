using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewCandidate
{
    public OpeningViewCandidate(
        long elementId,
        string categoryKey,
        string categoryName,
        string familyName,
        string typeName,
        string levelName,
        string viewName,
        XYZ origin,
        XYZ facingDirection,
        string orientationSource,
        bool orientationFallback,
        XYZ boundingBoxMin,
        XYZ boundingBoxMax,
        long? elevationViewTypeId,
        long? viewTemplateId,
        string message,
        bool canApply)
    {
        ElementId = elementId;
        CategoryKey = categoryKey;
        CategoryName = categoryName;
        FamilyName = familyName;
        TypeName = typeName;
        LevelName = levelName;
        ViewName = viewName;
        Origin = origin;
        FacingDirection = facingDirection;
        OrientationSource = OpeningViewOrientationSources.NormalizeKey(orientationSource);
        OrientationFallback = orientationFallback;
        BoundingBoxMin = boundingBoxMin;
        BoundingBoxMax = boundingBoxMax;
        ElevationViewTypeId = elevationViewTypeId;
        ViewTemplateId = viewTemplateId;
        Message = message;
        CanApply = canApply;
    }

    public long ElementId { get; }

    public string CategoryKey { get; }

    public string CategoryName { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string LevelName { get; }

    public string ViewName { get; }

    public XYZ Origin { get; }

    public XYZ FacingDirection { get; }

    public string OrientationSource { get; }

    public bool OrientationFallback { get; }

    public string OrientationSourceDisplay => OrientationFallback
        ? $"{OpeningViewOrientationSources.GetDisplayName(OpeningViewOrientationSources.HostWall)} -> {OpeningViewOrientationSources.GetDisplayName(OpeningViewOrientationSources.ElementFacing)}"
        : OpeningViewOrientationSources.GetDisplayName(OrientationSource);

    public XYZ BoundingBoxMin { get; }

    public XYZ BoundingBoxMax { get; }

    public long? ElevationViewTypeId { get; }

    public long? ViewTemplateId { get; }

    public string Message { get; }

    public bool CanApply { get; }

    public OpeningViewRow ToRow()
    {
        return new OpeningViewRow(
            ElementId,
            CategoryName,
            FamilyName,
            TypeName,
            LevelName,
            ViewName,
            OrientationSourceDisplay,
            CanApply ? OpeningViewStatuses.Ready : OpeningViewStatuses.Skipped,
            Message,
            CanApply);
    }
}
