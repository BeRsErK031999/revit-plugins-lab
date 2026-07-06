using System.IO;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Modules.BimTools.ParaManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParaManager;

public sealed class ParameterCsvImportServiceTests
{
    [Fact]
    public void ReadParsesSemicolonCsvWithCommaSeparatedCategories()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paramanager-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            path,
            """
            ParameterName;SharedGroup;BindingType;Categories;GroupUnder;DataType;Visible;UserModifiable;Description
            BIM_Раздел;BIM;Instance;Walls,Doors,Windows;Identity Data;Text;true;true;Раздел модели
            """);

        try
        {
            ParameterCsvImportService service = new();

            IReadOnlyList<ParameterImportRow> rows = service.Read(path);

            Assert.Single(rows);
            Assert.Equal("BIM_Раздел", rows[0].ParameterName);
            Assert.Equal(["Walls", "Doors", "Windows"], rows[0].CategoryNames);
            Assert.True(rows[0].TryGetBindingKind(out ParameterBindingKind bindingKind));
            Assert.Equal(ParameterBindingKind.Instance, bindingKind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateTemplateContainsRequiredHeaders()
    {
        ParameterCsvImportService service = new();

        string template = service.CreateTemplate();

        Assert.Contains("ParameterName;SharedGroup;BindingType;Categories;GroupUnder;DataType;Visible;UserModifiable;Description", template);
    }
}
