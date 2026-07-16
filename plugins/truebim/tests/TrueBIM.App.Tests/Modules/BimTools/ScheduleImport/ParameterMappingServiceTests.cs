using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ParameterMappingServiceTests
{
    [Fact]
    public void SuggestMappings_UsesExactAndUnitlessColumnNames()
    {
        ParsedTable table = new(
            "source.pdf",
            1,
            Array.Empty<ParsedRow>(),
            ["System Name", "Length, m", "Unknown"],
            Array.Empty<ParsedCell>(),
            1,
            Array.Empty<string>());
        ScheduleFieldOption[] fields =
        [
            CreateField("system", "System Name", "Instance"),
            CreateField("length", "Length", "Instance")
        ];

        IReadOnlyDictionary<string, string> mappings = new ParameterMappingService().SuggestMappings(table, fields);

        Assert.Equal("system", mappings["System Name"]);
        Assert.Equal("length", mappings["Length, m"]);
        Assert.False(mappings.ContainsKey("Unknown"));
    }

    [Fact]
    public void SuggestMappings_PrefersInstanceFieldWhenNamesAreEqual()
    {
        ParsedTable table = new(
            "source.pdf",
            1,
            Array.Empty<ParsedRow>(),
            ["Марка"],
            Array.Empty<ParsedCell>(),
            1,
            Array.Empty<string>());
        ScheduleFieldOption[] fields =
        [
            CreateField("type", "Марка", "ElementType"),
            CreateField("instance", "Марка", "Instance")
        ];

        IReadOnlyDictionary<string, string> mappings = new ParameterMappingService().SuggestMappings(table, fields);

        Assert.Equal("instance", mappings["Марка"]);
    }

    private static ScheduleFieldOption CreateField(string key, string name, string fieldType)
    {
        return new ScheduleFieldOption(key, name, $"{name} ({fieldType})", 1, 0, fieldType);
    }
}
