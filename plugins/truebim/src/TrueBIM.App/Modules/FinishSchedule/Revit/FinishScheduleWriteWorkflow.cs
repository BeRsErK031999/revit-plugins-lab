using System.Diagnostics;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishScheduleWriteWorkflow
{
    private readonly FinishSchedulePreviewService calculationService;
    private readonly RoomFinishWriteValueBuilder roomValueBuilder;
    private readonly FinishOwnershipValueBuilder ownershipValueBuilder;
    private readonly RoomFinishParameterWriter roomWriter;
    private readonly FinishOwnershipWriter ownershipWriter;
    private readonly FinishRoomSchedulePlanBuilder schedulePlanBuilder;
    private readonly FinishRoomScheduleBuilder scheduleBuilder;
    private readonly ITrueBimLogger logger;

    public FinishScheduleWriteWorkflow(
        FinishSchedulePreviewService calculationService,
        RoomFinishWriteValueBuilder roomValueBuilder,
        FinishOwnershipValueBuilder ownershipValueBuilder,
        RoomFinishParameterWriter roomWriter,
        FinishOwnershipWriter ownershipWriter,
        FinishRoomSchedulePlanBuilder schedulePlanBuilder,
        FinishRoomScheduleBuilder scheduleBuilder,
        ITrueBimLogger logger)
    {
        this.calculationService = calculationService ?? throw new ArgumentNullException(nameof(calculationService));
        this.roomValueBuilder = roomValueBuilder ?? throw new ArgumentNullException(nameof(roomValueBuilder));
        this.ownershipValueBuilder = ownershipValueBuilder ?? throw new ArgumentNullException(nameof(ownershipValueBuilder));
        this.roomWriter = roomWriter ?? throw new ArgumentNullException(nameof(roomWriter));
        this.ownershipWriter = ownershipWriter ?? throw new ArgumentNullException(nameof(ownershipWriter));
        this.schedulePlanBuilder = schedulePlanBuilder ?? throw new ArgumentNullException(nameof(schedulePlanBuilder));
        this.scheduleBuilder = scheduleBuilder ?? throw new ArgumentNullException(nameof(scheduleBuilder));
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
        FinishRoomSchedulePlan schedulePlan = schedulePlanBuilder.Build(
            settings,
            calculation.Build.RoomScope.SelectedRooms);
        FinishRoomSchedulePreflight schedulePreflight = scheduleBuilder.Preflight(document, schedulePlan);
        FinishScheduleWritePreview preview = new(
            calculation.Aggregation.Groups.Count,
            calculation.Aggregation.RoomOutputs.Count,
            roomPlan,
            ownershipPlan,
            calculation.Preview.Warnings,
            schedulePreflight,
            calculation.Preview,
            roomPlan.Changes.Concat(ownershipPlan.Changes)
                .Select(change => change.ElementId)
                .Distinct()
                .ToDictionary(id => id, id => DescribeElement(document, id)));
        logger.Info(
            $"Finish Schedule write plan prepared. Rooms={preview.RoomCount}; Groups={preview.GroupCount}; "
            + $"RoomChanges={roomPlan.Changes.Count}; OwnershipChanges={ownershipPlan.Changes.Count}; "
            + $"ScheduleAction={schedulePreflight.Action}; "
            + $"CriticalIssues={preview.Issues.Count(issue => issue.Severity == FinishWriteIssueSeverity.Critical)}; "
            + $"Warnings={preview.Issues.Count(issue => issue.Severity == FinishWriteIssueSeverity.Warning)}.");
        return preview;
    }

    public FinishScheduleWriteResult Apply(
        Document document,
        FinishScheduleWritePreview preview,
        FinishScheduleHeaderMode headerMode = FinishScheduleHeaderMode.Custom)
    {
        Stopwatch totalTimer = Stopwatch.StartNew();
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        if (!Enum.IsDefined(typeof(FinishScheduleHeaderMode), headerMode))
        {
            throw new ArgumentOutOfRangeException(nameof(headerMode), headerMode, null);
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
                $"Запись не начата: {documentIssue.Message}",
                Performance: CompleteApplyPerformance([], totalTimer));
        }

        if (!preview.CanApply)
        {
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.Blocked,
                0,
                0,
                preflightSkippedOwnership,
                preflightWarnings,
                "Запись не начата: обязательный preflight обнаружил критические ошибки.",
                Performance: CompleteApplyPerformance([], totalTimer));
        }

        if (!preview.RequiresTransaction)
        {
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.NoChanges,
                0,
                0,
                preflightSkippedOwnership,
                preflightWarnings,
                "Параметры и управляемая спецификация уже актуальны; транзакции не открывались.",
                preview.Schedule.ScheduleId.HasValue && preview.Schedule.Plan is not null
                    ? new FinishRoomScheduleApplyResult(
                        preview.Schedule.ScheduleId.Value,
                        preview.Schedule.Plan.ScheduleName,
                        FinishRoomScheduleAction.NoChanges)
                    : null,
                CompleteApplyPerformance([], totalTimer));
        }

        using TransactionGroup group = new(document, "TrueBIM: сформировать ведомость отделки");
        bool groupStarted = false;
        List<FinishScheduleStageTiming> timings = [];
        Stopwatch stageTimer = new();
        try
        {
            FinishTransactionStatus.EnsureStarted(group);
            groupStarted = true;
            scheduleBuilder.PreservePreviousVersions(document, preview.Schedule);
            stageTimer.Restart();
            FinishOwnershipApplyResult ownershipResult = ownershipWriter.Apply(
                document,
                preview.OwnershipPlan);
            timings.Add(StopStage(FinishScheduleStageNames.OwnershipWrite, stageTimer));

            stageTimer.Restart();
            int appliedRoomValues = roomWriter.Apply(document, preview.RoomPlan);
            timings.Add(StopStage(FinishScheduleStageNames.RoomWrite, stageTimer));

            stageTimer.Restart();
            FinishRoomScheduleApplyResult scheduleResult = scheduleBuilder.Apply(
                document,
                preview.Schedule,
                headerMode);
            timings.Add(StopStage(FinishScheduleStageNames.ScheduleWrite, stageTimer));
            FinishTransactionStatus.EnsureAssimilated(group);
            groupStarted = false;
            FinishSchedulePerformanceSummary performance = CompleteApplyPerformance(timings, totalTimer);

            string[] warnings = preflightWarnings
                .Concat(ownershipResult.Warnings)
                .Concat(GetHeaderModeWarnings(headerMode))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            logger.Info(
                $"Finish Schedule parameters written. RoomValues={appliedRoomValues}; "
                + $"OwnershipValues={ownershipResult.AppliedCount}; "
                + $"OwnershipSkipped={preflightSkippedOwnership + ownershipResult.SkippedCount}; "
                + $"ScheduleAction={scheduleResult.Action}; ScheduleId={scheduleResult.ScheduleId}; "
                + $"HeaderMode={headerMode}; "
                + $"Warnings={warnings.Length}; ElapsedMs={totalTimer.ElapsedMilliseconds}.");
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.Applied,
                appliedRoomValues,
                ownershipResult.AppliedCount,
                preflightSkippedOwnership + ownershipResult.SkippedCount,
                warnings,
                BuildSuccessMessage(scheduleResult, headerMode, appliedRoomValues, ownershipResult.AppliedCount),
                scheduleResult,
                performance);
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

            bool headerFormattingFailed = exception is FinishScheduleHeaderFormattingException;
            logger.Error(
                headerFormattingFailed
                    ? "Failed to format Finish Schedule custom header."
                    : "Failed to build Finish Schedule.",
                exception);
            return new FinishScheduleWriteResult(
                FinishScheduleWriteStatus.Failed,
                0,
                0,
                preflightSkippedOwnership,
                preflightWarnings,
                headerFormattingFailed
                    ? "Revit не смог оформить составную шапку. Изменения отменены; можно повторить формирование с простой шапкой или без неё."
                    : "Формирование отменено целиком; параметры и спецификация не оставлены в частично обновлённом состоянии.",
                Performance: CompleteApplyPerformance(timings, totalTimer),
                FailureKind: headerFormattingFailed
                    ? FinishScheduleWriteFailureKind.HeaderFormatting
                    : FinishScheduleWriteFailureKind.None);
        }
    }

    private static IEnumerable<string> GetHeaderModeWarnings(FinishScheduleHeaderMode headerMode)
    {
        return headerMode switch
        {
            FinishScheduleHeaderMode.Standard =>
            ["Ведомость сформирована с простой шапкой Revit без объединённых ячеек."],
            FinishScheduleHeaderMode.None =>
            ["Ведомость сформирована без названия и заголовков столбцов."],
            _ => []
        };
    }

    private static string BuildSuccessMessage(
        FinishRoomScheduleApplyResult schedule,
        FinishScheduleHeaderMode headerMode,
        int roomValues,
        int ownershipValues)
    {
        string operation = $"Создана «{schedule.ScheduleName}»";
        string header = headerMode switch
        {
            FinishScheduleHeaderMode.Standard => " с простой шапкой Revit",
            FinishScheduleHeaderMode.None => " без шапки",
            _ => string.Empty
        };
        string parameterMessage = roomValues + ownershipValues == 0
            ? " Параметры уже совпадают с расчётом и не перезаписывались."
            : $" Обновлено значений параметров: {roomValues + ownershipValues}.";
        return $"{operation}{header}. Прежние версии сохранены.{parameterMessage}";
    }

    private static string DescribeElement(Document document, long id)
    {
        Element? element = document.GetElement(RevitElementIds.Create(id));
        if (element is Autodesk.Revit.DB.Architecture.Room room)
        {
            string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
            return $"Помещение {room.Number} «{name}»";
        }

        return $"{element?.Category?.Name ?? "Элемент"} (ID {id})";
    }

    private static FinishScheduleStageTiming StopStage(string stage, Stopwatch timer)
    {
        timer.Stop();
        return new FinishScheduleStageTiming(stage, timer.ElapsedMilliseconds);
    }

    private static FinishSchedulePerformanceSummary CompleteApplyPerformance(
        IEnumerable<FinishScheduleStageTiming> stages,
        Stopwatch totalTimer)
    {
        totalTimer.Stop();
        return new FinishSchedulePerformanceSummary(
            stages.Concat(
            [
                new FinishScheduleStageTiming(
                    FinishScheduleStageNames.TotalApply,
                    totalTimer.ElapsedMilliseconds)
            ]));
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
