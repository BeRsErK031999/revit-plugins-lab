using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class SharedParameterDeletionPlanBuilderTests
{
    private readonly SharedParameterDeletionPlanBuilder builder = new();

    [Fact]
    public void Build_BlocksUnknownDryRunCascade()
    {
        SharedParameterDryRunResult dryRun = new(
            [new DryRunDeletedElement(9001, "MysteryElement", "Неизвестная зависимость", false)],
            true,
            [],
            []);

        SharedParameterDeletionPlan plan = builder.Build(
            SharedParameterTestData.Analysis(),
            dryRun,
            []);

        Assert.False(plan.CanExecuteSafely);
        Assert.Contains(plan.Blockers, blocker => blocker.Code == "UNKNOWN_CASCADE_DEPENDENCY");
        Assert.Contains(9001, plan.DryRunDeletedIds);
    }

    [Fact]
    public void Build_BlocksScheduleWithOpaqueCalculatedDependencies()
    {
        ScheduleFieldUsage schedule = new(
            501,
            "Ведомость",
            7,
            "Тестовый параметр",
            "Параметр",
            0,
            false,
            true,
            true,
            false,
            true,
            DetectionConfidence.Probable);

        SharedParameterDeletionPlan plan = builder.Build(
            SharedParameterTestData.Analysis(schedules: [schedule]),
            new SharedParameterDryRunResult([], true, [], []),
            []);

        ScheduleDeletionAction action = Assert.Single(plan.Schedules);
        Assert.Equal(DeletionActionSupport.Unsupported, action.Support);
        Assert.Equal(DeletionRisk.Blocking, action.Risk);
        Assert.Contains(plan.Blockers, blocker => blocker.Code == "SCHEDULE_DEPENDENCY_UNRESOLVED");
    }

    [Fact]
    public void Build_RequiresDeepAnalysisForFamilyRemoval()
    {
        ProjectFamilyPresence family = new(
            701,
            "Дверь",
            "Двери",
            FamilyPresenceStatus.Found,
            true,
            null);

        SharedParameterDeletionPlan plan = builder.Build(
            SharedParameterTestData.Analysis(families: [family]),
            new SharedParameterDryRunResult([], true, [], []),
            []);

        Assert.False(plan.CanExecuteSafely);
        Assert.Contains(plan.Blockers, blocker => blocker.Code == "FAMILY_DEEP_ANALYSIS_REQUIRED");
    }
}
