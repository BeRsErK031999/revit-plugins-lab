using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishRoomSchedulePlanBuilderTests
{
    [Fact]
    public void Build_AllCategories_UsesSpecificationOrderHeadingsAndWidths()
    {
        FinishRoomSchedulePlan plan = new FinishRoomSchedulePlanBuilder().Build(
            Settings(),
            [Room()]);

        Assert.Equal(
            [
                "Перечень помещений",
                "Стены или перегородки",
                "Площадь, м²",
                "Потолок",
                "Площадь, м²",
                "Пол",
                "Площадь, м²"
            ],
            plan.Columns.Select(column => column.Heading));
        Assert.Equal(
            [40d, 80d, 25d, 80d, 25d, 80d, 25d],
            plan.Columns.Select(column => column.WidthMillimeters));
        Assert.Equal(
            [
                FinishRoomScheduleColumnKind.RoomList,
                FinishRoomScheduleColumnKind.Description,
                FinishRoomScheduleColumnKind.Area,
                FinishRoomScheduleColumnKind.Description,
                FinishRoomScheduleColumnKind.Area,
                FinishRoomScheduleColumnKind.Description,
                FinishRoomScheduleColumnKind.Area
            ],
            plan.Columns.Select(column => column.Kind));
    }

    [Fact]
    public void Build_DisabledCategory_OmitsItsColumns()
    {
        FinishScheduleSettings settings = Settings() with
        {
            Ceilings = Settings().Ceilings with { IsEnabled = false }
        };

        FinishRoomSchedulePlan plan = new FinishRoomSchedulePlanBuilder().Build(settings, [Room()]);

        Assert.Equal(5, plan.Columns.Count);
        Assert.DoesNotContain(plan.Columns, column => column.Heading == "Потолок");
    }

    [Fact]
    public void Build_SectionScope_UsesStableRawValueAndParameterIdentity()
    {
        ParameterReference section = Reference("Section", 20, ParameterStorageKind.Integer);
        FinishScheduleSettings settings = Settings() with
        {
            Scope = new ReportScopeSettings(ReportScopeKind.Section, null, section, "Раздел 2")
        };
        FinishRoomCandidateSnapshot room = Room(new Dictionary<string, FinishParameterValueSnapshot>
        {
            [section.StableKey] = new("2", "Раздел 2")
        });

        FinishRoomSchedulePlan plan = new FinishRoomSchedulePlanBuilder().Build(settings, [room]);

        Assert.Equal(ReportScopeKind.Section, plan.ScopeFilter.Kind);
        Assert.Equal(ParameterStorageKind.Integer, plan.ScopeFilter.StorageKind);
        Assert.Equal("2", plan.ScopeFilter.RawValue);
        Assert.Contains(section.StableKey, plan.ParameterIdentities);
    }

    [Fact]
    public void Build_SameConfiguration_HasDeterministicHashThatChangesWithScope()
    {
        FinishRoomSchedulePlanBuilder builder = new();
        FinishScheduleSettings settings = Settings();

        FinishRoomSchedulePlan first = builder.Build(settings, [Room()]);
        FinishRoomSchedulePlan second = builder.Build(settings, [Room()]);
        FinishRoomSchedulePlan level = builder.Build(
            settings with
            {
                Scope = new ReportScopeSettings(ReportScopeKind.Level, 42, null, string.Empty)
            },
            [Room()]);

        Assert.Equal(first.SettingsHash, second.SettingsHash);
        Assert.NotEqual(first.SettingsHash, level.SettingsHash);
        Assert.Equal("42", level.ScopeFilter.RawValue);
    }

    private static FinishScheduleSettings Settings()
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        return defaults with
        {
            RoomListOutputParameter = Reference("Room list", 1),
            Walls = defaults.Walls with
            {
                OutputDescriptionParameter = Reference("Wall description", 2),
                OutputAreaParameter = Reference("Wall area", 3)
            },
            Ceilings = defaults.Ceilings with
            {
                OutputDescriptionParameter = Reference("Ceiling description", 4),
                OutputAreaParameter = Reference("Ceiling area", 5)
            },
            Floors = defaults.Floors with
            {
                OutputDescriptionParameter = Reference("Floor description", 6),
                OutputAreaParameter = Reference("Floor area", 7)
            }
        };
    }

    private static ParameterReference Reference(
        string name,
        long id,
        ParameterStorageKind storageKind = ParameterStorageKind.String)
    {
        return ParameterReference.Project(name, id, ParameterBindingKind.Instance, storageKind);
    }

    private static FinishRoomCandidateSnapshot Room(
        IReadOnlyDictionary<string, FinishParameterValueSnapshot>? values = null)
    {
        return new FinishRoomCandidateSnapshot(
            100,
            10,
            20,
            true,
            new AxisAlignedBox3D(0, 0, 0, 1, 1, 1),
            values);
    }
}
