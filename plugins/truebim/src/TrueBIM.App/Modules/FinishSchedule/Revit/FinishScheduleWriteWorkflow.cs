using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishScheduleWriteWorkflow
{
    private readonly FinishSchedulePreviewService calculationService;
    private readonly RoomFinishWriteValueBuilder roomValueBuilder;
    private readonly FinishOwnershipValueBuilder ownershipValueBuilder;
    private readonly RoomFinishParameterWriter roomWriter;
    private readonly FinishOwnershipWriter ownershipWriter;
    private readonly ITrueBimLogger logger;

    public FinishScheduleWriteWorkflow(
        FinishSchedulePreviewService calculationService,
        RoomFinishWriteValueBuilder roomValueBuilder,
        FinishOwnershipValueBuilder ownershipValueBuilder,
        RoomFinishParameterWriter roomWriter,
        FinishOwnershipWriter ownershipWriter,
        ITrueBimLogger logger)
    {
        this.calculationService = calculationService ?? throw new ArgumentNullException(nameof(calculationService));
        this.roomValueBuilder = roomValueBuilder ?? throw new ArgumentNullException(nameof(roomValueBuilder));
        this.ownershipValueBuilder = ownershipValueBuilder ?? throw new ArgumentNullException(nameof(ownershipValueBuilder));
        this.roomWriter = roomWriter ?? throw new ArgumentNullException(nameof(roomWriter));
        this.ownershipWriter = ownershipWriter ?? throw new ArgumentNullException(nameof(ownershipWriter));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FinishScheduleWritePreview Prepare(
        Document document,
        FinishScheduleSettings settings)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        FinishWriteIssue? documentIssue = ValidateDocument(document);
        if (documentIssue is not null)
        {
            return FinishScheduleWritePreview.Blocked(documentIssue);
        }

        FinishScheduleCalculationResult calculation = calculationService.BuildDetailed(document, settings);
        if (calculation.Aggregation is null || calculation.RoomSnapshots is null)
        {
            return FinishScheduleWritePreview.Blocked(new FinishWriteIssue(
                FinishWriteIssueCode.OutputConfigurationInvalid,
                FinishWriteIssueSeverity.Critical,
                "Не удалось подготовить агрегированные значения. Проверьте источник описания и идентификатор помещений."));
        }

        if (calculation.Build.RoomScope.SelectedRooms.Count == 0)
        {
            return FinishScheduleWritePreview.Blocked(new FinishWriteIssue(
                FinishWriteIssueCode.NoTargetRooms,
                FinishWriteIssueSeverity.Critical,
                "В выбранной области нет валидных помещений для записи."));
        }

        FinishParameterTargetBuildResult roomTargets = roomValueBuilder.Build(
            settings,
            calculation.Aggregation);
        FinishParameterTargetBuildResult ownershipTargets = ownershipValueBuilder.Build(
            settings,
            calculation.Build.InScopeElements,
            calculation.Quantities,
            calculation.RoomSnapshots);
        FinishWritePlan roomPlan = roomWriter.Plan(document, roomTargets);
        FinishWritePlan ownershipPlan = ownershipWriter.Plan(document, ownershipTargets);
        FinishScheduleWritePreview preview = new(
            calculation.Aggregation.Groups.Count,
            calculation.Aggregation.RoomOutputs.Count,
            roomPlan,
            ownershipPlan,
            calculation.Preview.Warnings);
        logger.Info(
            $"Finish Schedule write plan prepared. Rooms={preview.RoomCount}; Groups={preview.GroupCount}; "
            + $"RoomChanges={roomPlan.Changes.Count}; OwnershipChanges={ownershipPlan.Changes.Count}; "
            + $"CriticalIssues={preview.Issues.Count(issue => issue.Severity == FinishWriteIssueSeverity.Critical)}; "
            + $"Warnings={preview.Issues.Count(issue => issue.Severity == FinishWriteIssueSeverity.Warning)}.");
        return preview;
    }

    public FinishScheduleWriteResult Apply(
        Document document,
        FinishScheduleWritePreview preview)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        string[] preflightWarnings = preview.CalculationWarnings
            .Concat(preview.Issues
                .Where(issue => issue.Severity == FinishWriteIssueSeverity.Warning)
                .Select(issue => issue.Message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        int preflightSkippedOwnership = Math.Max(
            0,
            preview.OwnershipPlan.TargetElementCount
                - preview.OwnershipPlan.Changes.Count
                - preview.OwnershipPlan.UnchangedCount);
        FinishWriteIssue? documentIssue = ValidateDocument(document);
        if (documentIssue is not null)
        {
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.Blocked,
                0,
                0,
                preflightSkippedOwnership,
                preflightWarnings,
                $"Запись не начата: {documentIssue.Message}");
        }

        if (!preview.CanApply)
        {
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.Blocked,
                0,
                0,
                preflightSkippedOwnership,
                preflightWarnings,
                "Запись не начата: обязательный preflight обнаружил критические ошибки.");
        }

        if (preview.TotalChangeCount == 0)
        {
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.NoChanges,
                0,
                0,
                preflightSkippedOwnership,
                preflightWarnings,
                "Все целевые параметры уже содержат актуальные значения; транзакции не открывались.");
        }

        using TransactionGroup group = new(document, "TrueBIM: ведомость отделки — запись параметров");
        bool groupStarted = false;
        try
        {
            FinishTransactionStatus.EnsureStarted(group);
            groupStarted = true;
            FinishOwnershipApplyResult ownershipResult = ownershipWriter.Apply(
                document,
                preview.OwnershipPlan);
            int appliedRoomValues = roomWriter.Apply(document, preview.RoomPlan);
            FinishTransactionStatus.EnsureAssimilated(group);
            groupStarted = false;

            string[] warnings = preflightWarnings
                .Concat(ownershipResult.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            logger.Info(
                $"Finish Schedule parameters written. RoomValues={appliedRoomValues}; "
                + $"OwnershipValues={ownershipResult.AppliedCount}; "
                + $"OwnershipSkipped={preflightSkippedOwnership + ownershipResult.SkippedCount}; "
                + $"Warnings={warnings.Length}.");
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.Applied,
                appliedRoomValues,
                ownershipResult.AppliedCount,
                preflightSkippedOwnership + ownershipResult.SkippedCount,
                warnings,
                "Параметры помещений и включённая ownership-запись обновлены атомарно. Спецификация будет подключена в FS-008.");
        }
        catch (Exception exception)
        {
            if (groupStarted)
            {
                try
                {
                    FinishTransactionStatus.RollBackIfStarted(group);
                }
                catch (Exception rollbackException)
                {
                    logger.Error("Failed to roll back Finish Schedule transaction group.", rollbackException);
                }
            }

            logger.Error("Failed to write Finish Schedule parameters.", exception);
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.Failed,
                0,
                0,
                preflightSkippedOwnership,
                preflightWarnings,
                "Запись отменена целиком; параметры помещений не оставлены в частично обновлённом состоянии.");
        }
    }

    private static FinishWriteIssue? ValidateDocument(Document document)
    {
        if (document.IsFamilyDocument)
        {
            return new FinishWriteIssue(
                FinishWriteIssueCode.FamilyDocument,
                FinishWriteIssueSeverity.Critical,
                "Инструмент доступен только в проекте Revit, а не в редакторе семейства.");
        }

        if (document.IsReadOnly)
        {
            return new FinishWriteIssue(
                FinishWriteIssueCode.DocumentReadOnly,
                FinishWriteIssueSeverity.Critical,
                "Документ Revit доступен только для чтения.");
        }

        if (document.IsModifiable)
        {
            return new FinishWriteIssue(
                FinishWriteIssueCode.DocumentAlreadyModifiable,
                FinishWriteIssueSeverity.Critical,
                "В документе уже выполняется другая транзакция. Завершите её и повторите запуск.");
        }

        return null;
    }
}
