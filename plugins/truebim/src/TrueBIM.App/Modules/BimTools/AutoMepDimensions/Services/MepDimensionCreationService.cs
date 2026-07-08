using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;

public sealed class MepDimensionCreationService
{
    public MepDimensionApplyResult Apply(
        Document document,
        View activeView,
        IReadOnlyList<MepDimensionCandidate> candidates,
        IReadOnlyCollection<string> selectedCandidateIds,
        MepDimensionProfile profile,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(candidates, nameof(candidates));
        Guard.NotNull(selectedCandidateIds, nameof(selectedCandidateIds));
        Guard.NotNull(profile, nameof(profile));
        Guard.NotNull(logger, nameof(logger));

        List<MepDimensionReportRow> rows = [];
        using Transaction transaction = new(document, "TrueBIM Auto MEP Dimensions");
        transaction.Start();

        foreach (MepDimensionCandidate candidate in candidates.Where(candidate => selectedCandidateIds.Contains(candidate.CandidateId)))
        {
            try
            {
                ReferenceArray references = new();
                int missingReferences = 0;
                foreach (long elementIdValue in candidate.ElementIds)
                {
                    Element? element = document.GetElement(RevitElementIds.Create(elementIdValue));
                    if (element is null
                        || !MepDimensionReferenceResolver.TryResolve(element, activeView, profile.AllowElementReferenceFallback, out Reference? reference, out _))
                    {
                        missingReferences++;
                        continue;
                    }

                    references.Append(reference);
                }

                if (references.Size < 2)
                {
                    rows.Add(CreateReportRow(candidate, activeView, MepDimensionStatuses.Skipped, "Нужно минимум два доступных Reference для NewDimension.", missingReferences));
                    continue;
                }

                Line dimensionLine = Line.CreateBound(candidate.DimensionStart, candidate.DimensionEnd);
                Dimension dimension = document.Create.NewDimension(activeView, dimensionLine, references);
                rows.Add(CreateReportRow(
                    candidate,
                    activeView,
                    MepDimensionStatuses.Done,
                    $"Размерная цепочка создана: ElementId {RevitElementIds.GetValue(dimension.Id)}.",
                    missingReferences));
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to create MEP dimension candidate '{candidate.CandidateId}'.", exception);
                rows.Add(CreateReportRow(candidate, activeView, MepDimensionStatuses.Error, exception.Message, candidate.MissingReferenceCount));
            }
        }

        transaction.Commit();
        return new MepDimensionApplyResult(rows);
    }

    private static MepDimensionReportRow CreateReportRow(
        MepDimensionCandidate candidate,
        View activeView,
        string status,
        string message,
        int missingReferences)
    {
        return new MepDimensionReportRow(
            "Применение",
            activeView.Name,
            candidate.CandidateId,
            candidate.CategoryName,
            candidate.DirectionName,
            candidate.ElementCount,
            candidate.ReadyReferenceCount,
            missingReferences,
            status,
            message);
    }
}
