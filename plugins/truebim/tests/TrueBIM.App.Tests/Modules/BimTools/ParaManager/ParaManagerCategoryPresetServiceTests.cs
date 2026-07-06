using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Modules.BimTools.ParaManager.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParaManager;

public sealed class ParaManagerCategoryPresetServiceTests
{
    [Fact]
    public void SaveAndLoad_NormalizesCategoryPreset()
    {
        string directory = Path.Combine(Path.GetTempPath(), "truebim-paramanager-" + Guid.NewGuid());
        string path = Path.Combine(directory, "category-preset.json");
        try
        {
            ParaManagerCategoryPresetService service = new(path, new TestLogger());

            service.Save([" Walls ", "Doors", "walls"]);

            ParaManagerCategoryPresetService reloaded = new(path, new TestLogger());
            Assert.Equal(["Doors", "Walls"], reloaded.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WithCategories_ReplacesCategoriesAndKeepsParameterFields()
    {
        ParameterImportRow row = new(4, "BIM_Раздел", "BIM", "Instance", "Walls", "Identity Data", "Text", "true", "true", "Описание")
        {
            Status = ParameterImportStatus.WillCreate,
            Message = "Параметр будет создан."
        };

        ParameterImportRow updated = row.WithCategories("Doors,Windows");

        Assert.Equal("BIM_Раздел", updated.ParameterName);
        Assert.Equal(["Doors", "Windows"], updated.CategoryNames);
        Assert.Equal(ParameterImportStatus.WillCreate, updated.Status);
        Assert.Equal("Параметр будет создан.", updated.Message);
    }

    private sealed class TestLogger : ITrueBimLogger
    {
        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
