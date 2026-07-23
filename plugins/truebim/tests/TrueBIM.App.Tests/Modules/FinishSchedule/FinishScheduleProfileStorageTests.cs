using System.IO;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleProfileStorageTests
{
    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsSpecificationDefaults()
    {
        using TempDirectory temp = new();
        FinishScheduleProfileStorage storage = new(temp.SettingsPath, new TestLogger());

        FinishScheduleSettings settings = storage.Load();

        Assert.Equal(FinishScheduleSettings.CreateDefault(), settings);
        Assert.EndsWith("settings.json", storage.SettingsPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsStableParameterIdentities()
    {
        using TempDirectory temp = new();
        FinishScheduleProfileStorage storage = new(temp.SettingsPath, new TestLogger());
        Guid sharedGuid = Guid.Parse("b9c8ee1e-317d-4bc8-b788-d6ba0f2392a6");
        ParameterReference description = ParameterReference.BuiltIn(
            "Описание",
            -1002001,
            ParameterBindingKind.Type,
            ParameterStorageKind.String);
        ParameterReference customIdentifier = ParameterReference.Shared(
            "Код помещения",
            sharedGuid,
            501,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
        ParameterReference projectOutput = ParameterReference.Project(
            "Ведомость. Стены",
            601,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        FinishScheduleSettings settings = defaults with
        {
            DescriptionParameter = description,
            RoomIdentifier = new RoomIdentifierSettings(
                RoomIdentifierMode.CustomParameter,
                customIdentifier),
            WriteOwnership = true,
            Walls = defaults.Walls with
            {
                ClassificationValue = "Отделка стен",
                OwnershipParameter = projectOutput,
                OutputDescriptionParameter = projectOutput,
                OutputAreaParameter = ParameterReference.Project(
                    "Ведомость. Площадь стен",
                    602,
                    ParameterBindingKind.Instance,
                    ParameterStorageKind.String)
            },
            Floors = defaults.Floors with { IsEnabled = false },
            RoomListOutputParameter = customIdentifier,
            Scope = new ReportScopeSettings(
                ReportScopeKind.Section,
                null,
                ParameterReference.BuiltIn(
                    "Секция",
                    -1002002,
                    ParameterBindingKind.Instance,
                    ParameterStorageKind.Integer),
                "2"),
            ScheduleName = "АР. Ведомость отделки",
            ColumnWidths = new FinishScheduleColumnWidths(52, 93, 28)
        };

        storage.Save(settings);
        FinishScheduleSettings loaded = storage.Load();

        Assert.Equal(settings.DescriptionParameter!.StableKey, loaded.DescriptionParameter!.StableKey);
        Assert.Equal(
            settings.RoomIdentifier.CustomParameter!.StableKey,
            loaded.RoomIdentifier.CustomParameter!.StableKey);
        Assert.Equal(
            settings.Walls.OutputDescriptionParameter!.StableKey,
            loaded.Walls.OutputDescriptionParameter!.StableKey);
        Assert.Equal(settings.Scope.SectionParameter!.StableKey, loaded.Scope.SectionParameter!.StableKey);
        Assert.Equal(settings.Scope.SectionValue, loaded.Scope.SectionValue);
        Assert.Equal(settings.ScheduleName, loaded.ScheduleName);
        Assert.Equal(settings.EffectiveColumnWidths, loaded.EffectiveColumnWidths);
        Assert.False(loaded.Floors.IsEnabled);
        Assert.True(loaded.WriteOwnership);
    }

    [Fact]
    public void Load_WhenJsonIsCorrupted_ReturnsDefaultsAndLogsWarning()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.SettingsPath)!);
        File.WriteAllText(temp.SettingsPath, "{ broken json");
        TestLogger logger = new();
        FinishScheduleProfileStorage storage = new(temp.SettingsPath, logger);

        FinishScheduleSettings settings = storage.Load();

        Assert.Equal(FinishScheduleSettings.CreateDefault(), settings);
        Assert.Contains(logger.Warnings, message => message.Contains(temp.SettingsPath, StringComparison.Ordinal));
    }

    [Fact]
    public void Load_WhenStoredReferenceIsInvalid_DropsOnlyThatReference()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.SettingsPath)!);
        File.WriteAllText(
            temp.SettingsPath,
            """
            {
              "Version": 1,
              "DescriptionParameter": {
                "Name": "Состав",
                "IdentityKind": "Shared",
                "SharedParameterGuid": "00000000-0000-0000-0000-000000000000",
                "DefinitionElementId": 0,
                "BindingKind": "Type",
                "StorageKind": "String"
              },
              "ScheduleName": "Проверяемая ведомость"
            }
            """);
        FinishScheduleProfileStorage storage = new(temp.SettingsPath, new TestLogger());

        FinishScheduleSettings settings = storage.Load();

        Assert.Null(settings.DescriptionParameter);
        Assert.Equal("Проверяемая ведомость", settings.ScheduleName);
        Assert.True(settings.Walls.IsEnabled);
        Assert.Equal(
            FinishScheduleColumnWidths.Default,
            settings.EffectiveColumnWidths);
    }

    [Fact]
    public void SaveAndLoad_PreservesIncompleteTextForLiveValidation()
    {
        using TempDirectory temp = new();
        FinishScheduleProfileStorage storage = new(temp.SettingsPath, new TestLogger());
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        FinishScheduleSettings incomplete = defaults with
        {
            Walls = defaults.Walls with { ClassificationValue = "  " },
            ScheduleName = string.Empty
        };

        storage.Save(incomplete);
        FinishScheduleSettings loaded = storage.Load();

        Assert.Equal(string.Empty, loaded.Walls.ClassificationValue);
        Assert.Equal(string.Empty, loaded.ScheduleName);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "truebim-finish-schedule-tests-" + Guid.NewGuid());
            SettingsPath = System.IO.Path.Combine(Path, "settings.json");
        }

        public string Path { get; }

        public string SettingsPath { get; }

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
        public List<string> Warnings { get; } = [];

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
            Warnings.Add(message);
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
