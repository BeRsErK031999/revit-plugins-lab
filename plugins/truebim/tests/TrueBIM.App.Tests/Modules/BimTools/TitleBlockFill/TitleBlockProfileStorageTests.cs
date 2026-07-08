using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.TitleBlockFill;

public sealed class TitleBlockProfileStorageTests
{
    [Fact]
    public void Normalize_ReturnsDefaultRuleWhenRulesAreEmpty()
    {
        TitleBlockProfile profile = TitleBlockProfileStorage.Normalize(new TitleBlockProfile
        {
            Name = "  ",
            Rules = []
        });

        Assert.Equal("Рабочая документация", profile.Name);
        TitleBlockParameterRule rule = Assert.Single(profile.Rules);
        Assert.Equal(TitleBlockRuleTargets.Sheet, rule.Target);
        Assert.Equal(TitleBlockValueSources.StaticText, rule.Source);
    }

    [Fact]
    public void Save_RoundTripsNormalizedRules()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        TitleBlockProfileStorage storage = new(settingsPath, new TestLogger());

        storage.Save(new TitleBlockProfile
        {
            Name = "  РД  ",
            Rules =
            [
                new TitleBlockParameterRule
                {
                    IsEnabled = true,
                    Target = "wrong",
                    ParameterName = "  Дата выпуска  ",
                    Source = TitleBlockValueSources.Date,
                    Value = "  dd.MM.yyyy  "
                }
            ]
        });

        TitleBlockProfile loaded = storage.Load();

        Assert.Equal("РД", loaded.Name);
        TitleBlockParameterRule rule = Assert.Single(loaded.Rules);
        Assert.Equal(TitleBlockRuleTargets.Sheet, rule.Target);
        Assert.Equal("Дата выпуска", rule.ParameterName);
        Assert.Equal(TitleBlockValueSources.Date, rule.Source);
        Assert.Equal("dd.MM.yyyy", rule.Value);
    }

    [Fact]
    public void Normalize_KeepsFormulaSource()
    {
        TitleBlockProfile profile = TitleBlockProfileStorage.Normalize(new TitleBlockProfile
        {
            Name = "Formula",
            Rules =
            [
                new TitleBlockParameterRule
                {
                    Target = TitleBlockRuleTargets.TitleBlock,
                    ParameterName = "SheetCode",
                    Source = TitleBlockValueSources.Formula,
                    Value = "{SheetNumber}_{Date:yyyy-MM-dd}"
                }
            ]
        });

        TitleBlockParameterRule rule = Assert.Single(profile.Rules);
        Assert.Equal(TitleBlockRuleTargets.TitleBlock, rule.Target);
        Assert.Equal(TitleBlockValueSources.Formula, rule.Source);
        Assert.Equal("{SheetNumber}_{Date:yyyy-MM-dd}", rule.Value);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-title-block-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
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
