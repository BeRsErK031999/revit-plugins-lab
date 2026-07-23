using TrueBIM.App.Modules.SharedParameters.Models;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

internal static class SharedParameterTestData
{
    internal static SharedParameterDescriptor Parameter(
        long id = 101,
        string name = "Тестовый параметр",
        Guid? guid = null,
        SharedParameterBindingKind bindingKind = SharedParameterBindingKind.Instance,
        bool hasProjectBinding = true)
    {
        return new SharedParameterDescriptor(
            id,
            guid ?? Guid.Parse("3e04db00-0fa8-4aed-9fc3-8f47cb91ade1"),
            name,
            "Text",
            "Identity Data",
            bindingKind,
            [new CategoryDescriptor(2000011, "Стены")],
            hasProjectBinding,
            false);
    }

    internal static SharedParameterProjectAnalysis Analysis(
        SharedParameterDescriptor? parameter = null,
        IReadOnlyList<ElementParameterUsage>? elements = null,
        IReadOnlyList<ElementUsageAggregate>? aggregates = null,
        IReadOnlyList<ScheduleFieldUsage>? schedules = null,
        IReadOnlyList<ViewFilterUsage>? filters = null,
        IReadOnlyList<GlobalParameterAssociationUsage>? globalParameters = null,
        IReadOnlyList<ProjectFamilyPresence>? families = null,
        IReadOnlyList<DeletionBlocker>? blockers = null,
        IReadOnlyList<DeletionWarning>? warnings = null)
    {
        return new SharedParameterProjectAnalysis(
            new DocumentIdentity("TestProject", @"C:\Models\TestProject.rvt", "2025", false, false),
            parameter ?? Parameter(),
            new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.FromHours(7)),
            elements ?? [],
            aggregates ?? [],
            schedules ?? [],
            filters ?? [],
            globalParameters ?? [],
            families ?? [],
            blockers ?? [],
            warnings ?? [],
            []);
    }
}
