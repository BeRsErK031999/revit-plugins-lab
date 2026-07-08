using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewCreationService
{
    private const double FeetPerMillimeter = 1.0 / 304.8;

    public OpeningViewApplyResult Apply(
        Document document,
        ViewPlan activePlan,
        IReadOnlyList<OpeningViewCandidate> candidates,
        IReadOnlyCollection<long> selectedElementIds,
        OpeningViewProfile profile,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activePlan, nameof(activePlan));
        Guard.NotNull(candidates, nameof(candidates));
        Guard.NotNull(selectedElementIds, nameof(selectedElementIds));
        Guard.NotNull(profile, nameof(profile));
        Guard.NotNull(logger, nameof(logger));

        List<OpeningViewReportRow> rows = [];
        HashSet<string> existingViewNames = CollectExistingViewNames(document);

        using Transaction transaction = new(document, "TrueBIM Opening Views");
        transaction.Start();

        foreach (OpeningViewCandidate candidate in candidates.Where(candidate => selectedElementIds.Contains(candidate.ElementId)))
        {
            if (!candidate.CanApply || candidate.ElevationViewTypeId is null)
            {
                rows.Add(CreateReportRow(candidate, activePlan, OpeningViewStatuses.Skipped, candidate.Message));
                continue;
            }

            if (existingViewNames.Contains(candidate.ViewName))
            {
                rows.Add(CreateReportRow(candidate, activePlan, OpeningViewStatuses.Skipped, "Вид с таким именем уже существует."));
                continue;
            }

            using SubTransaction subTransaction = new(document);
            try
            {
                subTransaction.Start();
                ElevationMarker marker = ElevationMarker.CreateElevationMarker(
                    document,
                    RevitElementIds.Create(candidate.ElevationViewTypeId.Value),
                    candidate.Origin,
                    profile.Scale);
                ViewSection view = marker.CreateElevation(document, activePlan.Id, 0);
                document.Regenerate();

                AlignMarkerToDirection(document, marker, view, candidate.Origin, candidate.FacingDirection);
                document.Regenerate();

                view.Name = candidate.ViewName;
                ApplyTemplate(view, candidate.ViewTemplateId);
                ConfigureCrop(view, candidate, profile);
                subTransaction.Commit();
                existingViewNames.Add(candidate.ViewName);

                rows.Add(CreateReportRow(
                    candidate,
                    activePlan,
                    OpeningViewStatuses.Created,
                    $"Создан вид: {candidate.ViewName}."));
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to create opening view for ElementId {candidate.ElementId}.", exception);
                if (subTransaction.HasStarted())
                {
                    subTransaction.RollBack();
                }

                rows.Add(CreateReportRow(candidate, activePlan, OpeningViewStatuses.Error, exception.Message));
            }
        }

        transaction.Commit();
        return new OpeningViewApplyResult(rows);
    }

    private static void ApplyTemplate(ViewSection view, long? templateId)
    {
        if (templateId is not > 0)
        {
            return;
        }

        view.ViewTemplateId = RevitElementIds.Create(templateId.Value);
    }

    private static void AlignMarkerToDirection(
        Document document,
        ElevationMarker marker,
        ViewSection view,
        XYZ origin,
        XYZ targetDirection)
    {
        XYZ currentDirection = NormalizeHorizontal(view.ViewDirection, XYZ.BasisY);
        XYZ target = NormalizeHorizontal(targetDirection, XYZ.BasisY);
        double dot = Clamp(currentDirection.DotProduct(target), -1, 1);
        double crossZ = currentDirection.CrossProduct(target).Z;
        double angle = Math.Atan2(crossZ, dot);
        if (Math.Abs(angle) < 1e-6)
        {
            return;
        }

        Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
        ElementTransformUtils.RotateElement(document, marker.Id, axis, angle);
    }

    private static void ConfigureCrop(
        ViewSection view,
        OpeningViewCandidate candidate,
        OpeningViewProfile profile)
    {
        BoundingBoxXYZ cropBox = view.CropBox;
        Transform inverse = cropBox.Transform.Inverse;
        IReadOnlyList<XYZ> corners = GetCorners(candidate.BoundingBoxMin, candidate.BoundingBoxMax)
            .Select(inverse.OfPoint)
            .ToList();
        double margin = profile.CropMarginMm * FeetPerMillimeter;
        double depth = profile.DepthMarginMm * FeetPerMillimeter;
        double minX = corners.Min(point => point.X) - margin;
        double maxX = corners.Max(point => point.X) + margin;
        double minY = corners.Min(point => point.Y) - margin;
        double maxY = corners.Max(point => point.Y) + margin;
        double minZ = corners.Min(point => point.Z) - depth;
        double maxZ = corners.Max(point => point.Z) + depth;
        EnsureMinimumSize(ref minX, ref maxX, 1.0);
        EnsureMinimumSize(ref minY, ref maxY, 1.0);
        EnsureMinimumSize(ref minZ, ref maxZ, 1.0);

        cropBox.Min = new XYZ(minX, minY, minZ);
        cropBox.Max = new XYZ(maxX, maxY, maxZ);
        view.CropBox = cropBox;
        view.CropBoxActive = true;
        view.CropBoxVisible = true;
    }

    private static IReadOnlyList<XYZ> GetCorners(XYZ min, XYZ max)
    {
        return
        [
            new XYZ(min.X, min.Y, min.Z),
            new XYZ(max.X, min.Y, min.Z),
            new XYZ(min.X, max.Y, min.Z),
            new XYZ(max.X, max.Y, min.Z),
            new XYZ(min.X, min.Y, max.Z),
            new XYZ(max.X, min.Y, max.Z),
            new XYZ(min.X, max.Y, max.Z),
            new XYZ(max.X, max.Y, max.Z)
        ];
    }

    private static HashSet<string> CollectExistingViewNames(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate)
            .Select(view => view.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
    }

    private static OpeningViewReportRow CreateReportRow(
        OpeningViewCandidate candidate,
        ViewPlan activePlan,
        string status,
        string message)
    {
        return new OpeningViewReportRow(
            "Применение",
            activePlan.Name,
            candidate.ElementId,
            candidate.CategoryName,
            candidate.FamilyName,
            candidate.TypeName,
            candidate.LevelName,
            candidate.ViewName,
            status,
            message);
    }

    private static XYZ NormalizeHorizontal(XYZ value, XYZ fallback)
    {
        XYZ horizontal = new(value.X, value.Y, 0);
        if (horizontal.GetLength() < 1e-6)
        {
            horizontal = new(fallback.X, fallback.Y, 0);
        }

        return horizontal.GetLength() < 1e-6
            ? XYZ.BasisY
            : horizontal.Normalize();
    }

    private static void EnsureMinimumSize(ref double min, ref double max, double minimum)
    {
        if (max - min >= minimum)
        {
            return;
        }

        double center = (min + max) * 0.5;
        min = center - minimum * 0.5;
        max = center + minimum * 0.5;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
