using System.IO;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleConfigurationStorageTests
{
    [Fact]
    public void ExportImport_RoundTripsCoordinatorFieldsAndPreservesCalculationSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"truebim-finish-config-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "config.json");
        try
        {
            FinishScheduleConfigurationStorage storage = new();
            FinishScheduleSettings source = CreateSettings() with
            {
                DescriptionParameter = Shared("Состав", "11111111-1111-1111-1111-111111111111", ParameterBindingKind.Type),
                RoomIdentifier = new RoomIdentifierSettings(
                    RoomIdentifierMode.CustomParameter,
                    Shared("Код помещения", "22222222-2222-2222-2222-222222222222")),
                RoomListOutputParameter = Shared("Список", "33333333-3333-3333-3333-333333333333"),
                Walls = CreateSettings().Walls with
                {
                    ClassificationValue = "Wall finish",
                    OutputDescriptionParameter = Shared("Стены описание", "44444444-4444-4444-4444-444444444444"),
                    OutputAreaParameter = Shared("Стены площадь", "55555555-5555-5555-5555-555555555555")
                }
            };
            storage.Export(path, source);
            FinishScheduleSettings current = CreateSettings() with
            {
                WriteOwnership = true,
                Scope = new ReportScopeSettings(ReportScopeKind.Level, 901, null, string.Empty),
                ScheduleName = "Текущий расчёт"
            };

            FinishScheduleSettings imported = storage.Import(path, current);

            Assert.Equal(source.DescriptionParameter!.StableKey, imported.DescriptionParameter!.StableKey);
            Assert.Equal(RoomIdentifierMode.CustomParameter, imported.RoomIdentifier.Mode);
            Assert.Equal("Wall finish", imported.Walls.ClassificationValue);
            Assert.True(imported.WriteOwnership);
            Assert.Equal(901, imported.Scope.LevelId);
            Assert.Equal("Текущий расчёт", imported.ScheduleName);
            Assert.Equal(current.Walls.OwnershipParameter, imported.Walls.OwnershipParameter);
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
    public void Import_RejectsUnsupportedVersion()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Version\":99}");

            Assert.Throws<InvalidDataException>(() =>
                new FinishScheduleConfigurationStorage().Import(path, CreateSettings()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static FinishScheduleSettings CreateSettings()
    {
        return FinishScheduleSettings.CreateDefault() with
        {
            Walls = FinishScheduleSettings.CreateDefault().Walls with
            {
                OwnershipParameter = Shared("Стены ownership", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            }
        };
    }

    private static ParameterReference Shared(
        string name,
        string guid,
        ParameterBindingKind bindingKind = ParameterBindingKind.Instance)
    {
        return ParameterReference.Shared(
            name,
            Guid.Parse(guid),
            definitionElementId: Math.Abs(name.GetHashCode()) + 1L,
            bindingKind,
            ParameterStorageKind.String);
    }
}
