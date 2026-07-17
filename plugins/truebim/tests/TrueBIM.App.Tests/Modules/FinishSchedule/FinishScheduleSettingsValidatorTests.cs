using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleSettingsValidatorTests
{
    private static readonly FinishScheduleParameterCategories Categories = new(
        new ParameterCategoryReference(1, "Помещения"),
        new ParameterCategoryReference(2, "Стены"),
        new ParameterCategoryReference(3, "Перекрытия"));

    [Fact]
    public void Validate_AcceptsCompleteCompatibleSettings()
    {
        TestProfile profile = CreateProfile();

        FinishScheduleValidationResult result = CreateValidator().Validate(
            profile.Settings,
            profile.Catalog,
            Categories);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_DefaultsExplainAllRequiredSelections()
    {
        FinishScheduleValidationResult result = CreateValidator().Validate(
            FinishScheduleSettings.CreateDefault(),
            new ParameterCatalog([]),
            Categories);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "description_parameter.missing");
        Assert.Contains(result.Issues, issue => issue.Code == "room_list_output.missing");
        Assert.Contains(result.Issues, issue => issue.Code == "walls.output_description.missing");
        Assert.Contains(result.Issues, issue => issue.Code == "floors.output_area.missing");
        Assert.Contains(result.Issues, issue => issue.Code == "ceilings.output_area.missing");
    }

    [Fact]
    public void Validate_RejectsDuplicateOutputsAndIdentifierConflict()
    {
        TestProfile profile = CreateProfile();
        FinishScheduleSettings settings = profile.Settings with
        {
            RoomIdentifier = new RoomIdentifierSettings(
                RoomIdentifierMode.CustomParameter,
                profile.RoomList),
            Walls = profile.Settings.Walls with
            {
                OutputDescriptionParameter = profile.RoomList
            }
        };

        FinishScheduleValidationResult result = CreateValidator().Validate(
            settings,
            profile.Catalog,
            Categories);

        Assert.Contains(result.Issues, issue => issue.Code == "outputs.duplicate_parameter");
        Assert.Contains(result.Issues, issue => issue.Code == "room_identifier.output_conflict");
    }

    [Fact]
    public void Validate_DisabledCategoriesDoNotRequireOutputsOrDescriptionApplicability()
    {
        TestProfile profile = CreateProfile(descriptionCategoryIds: [Categories.Walls.Id]);
        FinishScheduleSettings settings = profile.Settings with
        {
            Floors = profile.Settings.Floors with
            {
                IsEnabled = false,
                OutputDescriptionParameter = null,
                OutputAreaParameter = null
            },
            Ceilings = profile.Settings.Ceilings with
            {
                IsEnabled = false,
                OutputDescriptionParameter = null,
                OutputAreaParameter = null
            }
        };

        FinishScheduleValidationResult result = CreateValidator().Validate(
            settings,
            profile.Catalog,
            Categories);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsIncompleteSectionScope()
    {
        TestProfile profile = CreateProfile();
        ParameterReference staleReference = ParameterReference.Project(
            "Секция",
            999,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
        FinishScheduleSettings settings = profile.Settings with
        {
            Scope = new ReportScopeSettings(
                ReportScopeKind.Section,
                LevelId: null,
                SectionParameter: staleReference,
                SectionValue: string.Empty)
        };

        FinishScheduleValidationResult result = CreateValidator().Validate(
            settings,
            profile.Catalog,
            Categories);

        Assert.Contains(result.Issues, issue => issue.Code == "scope.section_parameter.not_found");
        Assert.Contains(result.Issues, issue => issue.Code == "scope.section_value.empty");
    }

    private static FinishScheduleSettingsValidator CreateValidator()
    {
        return new FinishScheduleSettingsValidator(new ParameterCatalogMatcher());
    }

    private static TestProfile CreateProfile(IEnumerable<long>? descriptionCategoryIds = null)
    {
        ParameterReference description = CreateParameter(
            100,
            "Описание отделки",
            ParameterBindingKind.Type);
        ParameterReference roomList = CreateParameter(101, "Список помещений");
        ParameterReference wallsDescription = CreateParameter(102, "Стены. Описание");
        ParameterReference wallsArea = CreateParameter(103, "Стены. Площадь");
        ParameterReference floorsDescription = CreateParameter(104, "Полы. Описание");
        ParameterReference floorsArea = CreateParameter(105, "Полы. Площадь");
        ParameterReference ceilingsDescription = CreateParameter(106, "Потолки. Описание");
        ParameterReference ceilingsArea = CreateParameter(107, "Потолки. Площадь");

        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        FinishScheduleSettings settings = defaults with
        {
            DescriptionParameter = description,
            RoomListOutputParameter = roomList,
            Walls = defaults.Walls with
            {
                OutputDescriptionParameter = wallsDescription,
                OutputAreaParameter = wallsArea
            },
            Floors = defaults.Floors with
            {
                OutputDescriptionParameter = floorsDescription,
                OutputAreaParameter = floorsArea
            },
            Ceilings = defaults.Ceilings with
            {
                OutputDescriptionParameter = ceilingsDescription,
                OutputAreaParameter = ceilingsArea
            }
        };

        long[] sourceCategories = (descriptionCategoryIds
                ?? [Categories.Walls.Id, Categories.Floors.Id])
            .ToArray();
        ParameterCatalog catalog = new(
        [
            CreateItem(description, sourceCategories, writable: false),
            CreateItem(roomList, [Categories.Rooms.Id]),
            CreateItem(wallsDescription, [Categories.Rooms.Id]),
            CreateItem(wallsArea, [Categories.Rooms.Id]),
            CreateItem(floorsDescription, [Categories.Rooms.Id]),
            CreateItem(floorsArea, [Categories.Rooms.Id]),
            CreateItem(ceilingsDescription, [Categories.Rooms.Id]),
            CreateItem(ceilingsArea, [Categories.Rooms.Id])
        ]);

        return new TestProfile(settings, catalog, roomList);
    }

    private static ParameterReference CreateParameter(
        long definitionId,
        string name,
        ParameterBindingKind bindingKind = ParameterBindingKind.Instance)
    {
        return ParameterReference.Project(
            name,
            definitionId,
            bindingKind,
            ParameterStorageKind.String);
    }

    private static ParameterCatalogItem CreateItem(
        ParameterReference reference,
        IEnumerable<long> categoryIds,
        bool writable = true)
    {
        return new ParameterCatalogItem(
            reference,
            categoryIds,
            sampleCount: 1,
            writableSampleCount: writable ? 1 : 0,
            readOnlySampleCount: writable ? 0 : 1);
    }

    private sealed record TestProfile(
        FinishScheduleSettings Settings,
        ParameterCatalog Catalog,
        ParameterReference RoomList);
}
