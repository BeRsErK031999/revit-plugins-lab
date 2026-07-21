using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishScheduleDiagnosticGuidanceBuilder
{
    public static IReadOnlyList<string> Build(FinishSchedulePreviewResult preview)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        List<string> guidance = [];
        AppendCategory(
            guidance,
            "Полы",
            FinishPreviewCategory.Floors,
            preview.Floors,
            preview.Quantities?.Floors,
            preview.GeometryWarnings);
        AppendCategory(
            guidance,
            "Потолки",
            FinishPreviewCategory.Ceilings,
            preview.Ceilings,
            preview.Quantities?.Ceilings,
            preview.GeometryWarnings);
        return guidance;
    }

    private static void AppendCategory(
        List<string> guidance,
        string name,
        FinishPreviewCategory category,
        FinishPreviewCategoryCounts counts,
        FinishQuantityCategorySummary? quantities,
        IReadOnlyList<FinishGeometryWarning> warnings)
    {
        FinishGeometryWarning[] criticalWarnings = warnings
            .Where(warning => warning.Category == category)
            .Where(FinishGeometryWarningClassifier.AffectsScheduleValue)
            .ToArray();
        bool hasNoGeometryLinks = counts.InScope > 0
            && quantities is not null
            && quantities.OccurrenceCount == 0;
        if (criticalWarnings.Length == 0 && !hasNoGeometryLinks)
        {
            return;
        }

        int affectedElements = criticalWarnings
            .Where(warning => warning.ElementId.HasValue)
            .Select(warning => warning.ElementId!.Value)
            .Distinct()
            .Count();
        int affectedRooms = criticalWarnings
            .Where(warning => warning.RoomId.HasValue)
            .Select(warning => warning.RoomId!.Value)
            .Distinct()
            .Count();
        guidance.Add(criticalWarnings.Length > 0
            ? $"{name} определены не полностью: проблемных элементов — {affectedElements}; затронуто помещений — {affectedRooms}."
            : $"{name} не связаны с помещениями: элементов в области — {counts.InScope}; геометрических связей — 0.");
        guidance.Add(category == FinishPreviewCategory.Ceilings
            ? "Для потолков проверьте верхнюю границу помещений, флаг «Граница помещения» и фактический контакт. Один потолок может учитываться сразу в нескольких помещениях — делить его по помещениям не требуется."
            : "Для полов проверьте нижнюю границу помещений и фактический контакт. Один пол может учитываться сразу в нескольких помещениях — делить его по помещениям не требуется.");
    }
}
