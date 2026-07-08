using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Services;

public sealed class AutoTagPlacementService
{
    public bool TryGetTagPoint(Element element, View activeView, out XYZ point, out string message)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(activeView, nameof(activeView));

        if (element.Location is LocationPoint locationPoint && IsValidPoint(locationPoint.Point))
        {
            point = locationPoint.Point;
            message = string.Empty;
            return true;
        }

        if (element.Location is LocationCurve locationCurve)
        {
            try
            {
                XYZ midpoint = locationCurve.Curve.Evaluate(0.5, normalized: true);
                if (IsValidPoint(midpoint))
                {
                    point = midpoint;
                    message = string.Empty;
                    return true;
                }
            }
            catch (Exception)
            {
            }
        }

        BoundingBoxXYZ? boundingBox = element.get_BoundingBox(activeView) ?? element.get_BoundingBox(null);
        if (boundingBox is not null)
        {
            XYZ center = (boundingBox.Min + boundingBox.Max) * 0.5;
            if (IsValidPoint(center))
            {
                point = center;
                message = string.Empty;
                return true;
            }
        }

        point = XYZ.Zero;
        message = "Не удалось определить точку марки: нет Location и bounding box.";
        return false;
    }

    public AutoTagApplyResult Apply(
        Document document,
        View activeView,
        IReadOnlyList<AutoTagElementRow> rows,
        AutoTagTypeOption defaultTagType,
        IReadOnlyDictionary<long, AutoTagTypeOption> tagTypesByCategory,
        bool onlyUntagged,
        bool useLeader,
        double offsetRightMm,
        double offsetUpMm,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(rows, nameof(rows));
        Guard.NotNull(defaultTagType, nameof(defaultTagType));
        Guard.NotNull(tagTypesByCategory, nameof(tagTypesByCategory));
        Guard.NotNull(logger, nameof(logger));

        List<AutoTagReportRow> reportRows = [];
        AutoTagExistingTagIndex existingTagIndex = AutoTagExistingTagIndex.Create(document, activeView, logger);
        using Transaction transaction = new(document, "TrueBIM Auto Tags");
        transaction.Start();

        foreach (AutoTagElementRow row in rows.Where(row => row.IsSelected && row.CanApply))
        {
            AutoTagTypeOption tagType = tagTypesByCategory.TryGetValue(row.CategoryId, out AutoTagTypeOption? categoryTagType)
                ? categoryTagType
                : defaultTagType;

            try
            {
                ElementId elementId = RevitElementIds.Create(row.ElementId);
                Element? element = document.GetElement(elementId);
                if (element is null)
                {
                    reportRows.Add(CreateReportRow(activeView, row, tagType, AutoTagStatuses.Error, "Элемент не найден."));
                    continue;
                }

                if (onlyUntagged && existingTagIndex.GetTagCount(element.Id) > 0)
                {
                    reportRows.Add(CreateReportRow(activeView, row, tagType, AutoTagStatuses.Skipped, "У элемента уже есть марка на активном виде."));
                    continue;
                }

                if (!TryGetTagPoint(element, activeView, out XYZ point, out string pointMessage))
                {
                    reportRows.Add(CreateReportRow(activeView, row, tagType, AutoTagStatuses.Skipped, pointMessage));
                    continue;
                }

                XYZ tagPoint = AutoTagPlacementOffset.Apply(point, activeView.RightDirection, activeView.UpDirection, offsetRightMm, offsetUpMm);
                IndependentTag tag = IndependentTag.Create(
                    document,
                    activeView.Id,
                    new Reference(element),
                    useLeader,
                    TagMode.TM_ADDBY_CATEGORY,
                    TagOrientation.Horizontal,
                    tagPoint);

                string offsetText = AutoTagPlacementOffset.FormatForReport(offsetRightMm, offsetUpMm);
                string message = AppendOffsetText("Марка создана.", offsetText);
                if (!tagType.IsAutomatic)
                {
                    try
                    {
                        tag.ChangeTypeId(RevitElementIds.Create(tagType.ElementId));
                        message = AppendOffsetText($"Марка создана, тип: {tagType.DisplayName}.", offsetText);
                    }
                    catch (Exception exception)
                    {
                        message = AppendOffsetText($"Марка создана автоматически, но выбранный тип не применён: {exception.Message}", offsetText);
                        logger.Warning($"Failed to change tag type for element {row.ElementId}: {exception.Message}");
                    }
                }

                reportRows.Add(CreateReportRow(activeView, row, tagType, AutoTagStatuses.Done, message));
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to create tag for element {row.ElementId}.", exception);
                reportRows.Add(CreateReportRow(activeView, row, tagType, AutoTagStatuses.Error, exception.Message));
            }
        }

        transaction.Commit();
        return new AutoTagApplyResult(reportRows);
    }

    private static AutoTagReportRow CreateReportRow(
        View activeView,
        AutoTagElementRow row,
        AutoTagTypeOption tagType,
        string status,
        string message)
    {
        return new AutoTagReportRow(
            "Применение",
            activeView.Name,
            row.ElementId,
            row.CategoryName,
            row.ElementName,
            tagType.DisplayName,
            status,
            message);
    }

    private static string AppendOffsetText(string message, string offsetText)
    {
        return string.IsNullOrWhiteSpace(offsetText) ? message : $"{message} {offsetText}";
    }

    private static bool IsValidPoint(XYZ point)
    {
        return IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
