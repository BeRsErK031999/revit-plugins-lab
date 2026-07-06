using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Modules.BimTools.ParaManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParaManager;

public sealed class ParaManagerValidationServiceTests
{
    [Fact]
    public void ValidateMarksNewRowsAsWillCreate()
    {
        ParameterImportRow row = new(2, "BIM_Раздел", "BIM", "Instance", "Walls,Doors", "Identity Data", "Text", "true", "true", string.Empty);
        ParaManagerValidationService service = new();

        service.Validate([row], new HashSet<string>(), category => category is "Walls" or "Doors");

        Assert.Equal(ParameterImportStatus.WillCreate, row.Status);
        Assert.True(row.CanApply);
    }

    [Fact]
    public void ValidateMarksExistingRowsAsWillUpdate()
    {
        ParameterImportRow row = new(2, "BIM_Раздел", "BIM", "Instance", "Walls", "Identity Data", "Text", "true", "true", string.Empty);
        ParaManagerValidationService service = new();

        service.Validate([row], new HashSet<string>(StringComparer.CurrentCultureIgnoreCase) { "BIM_Раздел" }, category => category == "Walls");

        Assert.Equal(ParameterImportStatus.WillUpdate, row.Status);
    }

    [Fact]
    public void ValidateRejectsUnsupportedDataType()
    {
        ParameterImportRow row = new(2, "BIM_Раздел", "BIM", "Instance", "Walls", "Identity Data", "Currency", "true", "true", string.Empty);
        ParaManagerValidationService service = new();

        service.Validate([row], new HashSet<string>(), category => category == "Walls");

        Assert.Equal(ParameterImportStatus.Invalid, row.Status);
        Assert.Contains("Тип данных", row.Message);
    }

    [Fact]
    public void ValidateRejectsMissingCategory()
    {
        ParameterImportRow row = new(2, "BIM_Раздел", "BIM", "Instance", "Unknown", "Identity Data", "Text", "true", "true", string.Empty);
        ParaManagerValidationService service = new();

        service.Validate([row], new HashSet<string>(), _ => false);

        Assert.Equal(ParameterImportStatus.Invalid, row.Status);
        Assert.Contains("Категории не найдены", row.Message);
    }
}
