using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleRegister;

public sealed class ScheduleRegisterSettingsAndNameTests
{
    [Fact]
    public void SettingsStorage_RoundTripsVersionSpecificSettings()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "settings-2022.json");
        ScheduleRegisterSettingsStorage storage = new(path, new TestLogger());

        storage.Save(new ScheduleRegisterSettings
        {
            FilterEnabled = true,
            FilterParameterName = "  BIM_Статус  ",
            ExcludedValue = "  Не специфицировать  ",
            TemplateProjectPath = "  Y:\\Template.rte  "
        });
        ScheduleRegisterSettings loaded = storage.Load();

        Assert.True(loaded.FilterEnabled);
        Assert.Equal("BIM_Статус", loaded.FilterParameterName);
        Assert.Equal("Не специфицировать", loaded.ExcludedValue);
        Assert.Equal("Y:\\Template.rte", loaded.TemplateProjectPath);
    }

    [Fact]
    public void Validate_AllowsDisabledFilterWithoutParameter()
    {
        IReadOnlyList<string> issues = ScheduleRegisterSettingsStorage.Validate(new ScheduleRegisterSettings
        {
            FilterEnabled = false,
            FilterParameterName = string.Empty,
            ExcludedValue = string.Empty
        });

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_RejectsEnabledFilterWithoutExplicitParameterAndValue()
    {
        IReadOnlyList<string> issues = ScheduleRegisterSettingsStorage.Validate(new ScheduleRegisterSettings
        {
            FilterEnabled = true,
            FilterParameterName = " ",
            ExcludedValue = " "
        });

        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void NameService_UsesFirstAvailableParenthesizedNumber()
    {
        string name = ScheduleRegisterNameService.CreateUniqueName(
        [
            ScheduleRegisterConstants.TemplateScheduleName,
            ScheduleRegisterConstants.TemplateScheduleName + " (2)",
            ScheduleRegisterConstants.TemplateScheduleName + " (4)"
        ]);

        Assert.Equal(ScheduleRegisterConstants.TemplateScheduleName + " (3)", name);
        Assert.True(ScheduleRegisterNameService.IsTemplateOrGeneratedCopy(name));
        Assert.False(ScheduleRegisterNameService.IsTemplateOrGeneratedCopy("Спецификация окон (3)"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-schedule-register-tests-" + Guid.NewGuid());
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
