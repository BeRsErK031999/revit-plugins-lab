using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleMappingConfigurationServiceTests
{
    [Fact]
    public void Validate_RequiresMappedFieldsAndFilterValues()
    {
        ParsedTable table = CreateTable();
        ScheduleFieldMapping[] mappings =
        [
            new("Марка", "instance:1", "Марка", 1, 0, ScheduleFilterRule.Equal, null)
        ];

        ScheduleMappingValidationResult result = new ScheduleMappingConfigurationService().Validate(
            table,
            42,
            mappings);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("укажите значение", StringComparison.CurrentCulture));
    }

    [Fact]
    public void Validate_RejectsDuplicateRevitFields()
    {
        ParsedTable table = CreateTable();
        ScheduleFieldMapping[] mappings =
        [
            new("Марка", "instance:1", "Марка", 1, 0, ScheduleFilterRule.None, null),
            new("Комментарий", "instance:1", "Марка", 1, 0, ScheduleFilterRule.None, null)
        ];

        ScheduleMappingValidationResult result = new ScheduleMappingConfigurationService().Validate(
            table,
            42,
            mappings);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("нельзя добавить дважды", StringComparison.CurrentCulture));
    }

    [Fact]
    public void CreateFingerprint_ChangesWhenFilterChanges()
    {
        ParsedTable table = CreateTable();
        ScheduleMappingConfigurationService service = new();
        ScheduleFieldMapping mapping = new(
            "Марка",
            "instance:1",
            "Марка",
            1,
            0,
            ScheduleFilterRule.Equal,
            "A-1");

        string first = service.CreateFingerprint(table, 42, [mapping]);
        string second = service.CreateFingerprint(
            table,
            42,
            [mapping with { FilterValue = "A-2" }]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Validate_AllowsExplicitlyIgnoredPdfColumns()
    {
        ParsedTable table = CreateTable();
        ScheduleFieldMapping[] mappings =
        [
            new("Марка", "instance:1", "Марка", 1, 0, ScheduleFilterRule.None, null)
        ];

        ScheduleMappingValidationResult result = new ScheduleMappingConfigurationService().Validate(
            table,
            42,
            mappings);

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.ConfigurationFingerprint);
        Assert.Contains(result.Warnings, warning => warning.Contains("Не будут добавлены", StringComparison.CurrentCulture));
    }

    private static ParsedTable CreateTable()
    {
        return new ParsedTable(
            "source.pdf",
            1,
            [new ParsedRow(0, ["Марка", "Комментарий"])],
            ["Марка", "Комментарий"],
            Array.Empty<ParsedCell>(),
            1,
            Array.Empty<string>());
    }
}
