using System.Reflection;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintPresetStorageTests
{
    public PrintPresetStorageTests()
    {
        LoadRevitApi();
    }

    [Fact]
    public void Load_ReturnsEmptyStateWhenPresetFileDoesNotExist()
    {
        using TempDirectory temp = new();
        PrintPresetStorage storage = new(Path.Combine(temp.Path, "print-presets.json"), new TestLogger());

        PrintPresetStoreState state = storage.Load();

        Assert.False(storage.StorageFileExists);
        Assert.Empty(state.Presets);
        Assert.Null(state.LastSelectedPresetName);
    }

    [Fact]
    public void Save_PersistsCompleteNormalizedCustomerPreset()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "nested", "print-presets.json");
        PrintPresetStorage storage = new(storagePath, new TestLogger());
        PrintSettings settings = new(
            @"C:\Exports\Customer A",
            "  {Номер листа}_{Имя листа}  ",
            IncludePlaceholders: true,
            ExportPdf: true,
            CombinePdf: true,
            "  {Номер проекта}_Customer A  ",
            PrintPdfColorMode.GrayScale,
            PrintPdfRasterQuality.High,
            AlwaysUseRasterPdf: false,
            ExportDwg: true,
            ExportDxf: false,
            ExportDwf: false,
            CombineDwg: true,
            DwgSetupName: "  Customer A DWG  ",
            DxfSetupName: null,
            CombinedDwgFileNameMask: "  {Номер проекта}_{Имя документа}  ");
        PrintPresetStoreState state = new()
        {
            Presets =
            [
                new PrintPreset
                {
                    Name = "  Заказчик А  ",
                    Settings = settings,
                    DwgProfile = new DwgExportProfile
                    {
                        ProfileName = "  Заказчик А DWG  ",
                        SourceRevitSetupName = "  Customer A DWG  ",
                        IsUserProfile = true
                    }
                }
            ],
            LastSelectedPresetName = "  Заказчик А  "
        };

        storage.Save(state);

        PrintPresetStoreState reloadedState = new PrintPresetStorage(storagePath, new TestLogger()).Load();
        PrintPreset preset = Assert.Single(reloadedState.Presets);
        Assert.Equal("Заказчик А", preset.Name);
        Assert.Equal("Заказчик А", reloadedState.LastSelectedPresetName);
        Assert.NotNull(preset.Settings);
        Assert.Equal(@"C:\Exports\Customer A", preset.Settings!.ExportFolder);
        Assert.Equal("{Номер листа}_{Имя листа}", preset.Settings.FileNameMask);
        Assert.True(preset.Settings.ExportPdf);
        Assert.True(preset.Settings.ExportDwg);
        Assert.True(preset.Settings.CombinePdf);
        Assert.True(preset.Settings.CombineDwg);
        Assert.Equal("{Номер проекта}_Customer A", preset.Settings.CombinedPdfFileName);
        Assert.Equal("{Номер проекта}_{Имя документа}", preset.Settings.CombinedDwgFileNameMask);
        Assert.Equal("Customer A DWG", preset.Settings.DwgSetupName);
        Assert.NotNull(preset.DwgProfile);
        Assert.Equal("Заказчик А DWG", preset.DwgProfile!.ProfileName);
        Assert.Equal("Customer A DWG", preset.DwgProfile.SourceRevitSetupName);
    }

    [Fact]
    public void Normalize_DeduplicatesPresetNamesAndRepairsSelection()
    {
        PrintPresetStoreState state = new()
        {
            Presets =
            [
                new PrintPreset { Name = "Заказчик А" },
                new PrintPreset { Name = " заказчик а " },
                new PrintPreset { Name = "Заказчик Б" }
            ],
            LastSelectedPresetName = "Удаленный пресет"
        };

        PrintPresetStoreState normalizedState = PrintPresetStorage.Normalize(state);

        Assert.Collection(
            normalizedState.Presets,
            preset => Assert.Equal("Заказчик А", preset.Name),
            preset => Assert.Equal("Заказчик Б", preset.Name));
        Assert.Equal("Заказчик А", normalizedState.LastSelectedPresetName);
    }

    [Fact]
    public void Load_RepairsNullPresetCollection()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "print-presets.json");
        File.WriteAllText(storagePath, "{ \"presets\": null, \"lastSelectedPresetName\": \"Missing\" }");

        PrintPresetStoreState state = new PrintPresetStorage(storagePath, new TestLogger()).Load();

        Assert.Empty(state.Presets);
        Assert.Null(state.LastSelectedPresetName);
    }

    [Fact]
    public void Load_CollapsesLegacySeparateAndCombinedPdfPresetToCombinedFlag()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "print-presets.json");
        File.WriteAllText(
            storagePath,
            """
            {
              "presets": [
                {
                  "name": "Legacy PDF",
                  "settings": {
                    "exportPdf": true,
                    "combinePdf": true,
                    "exportSeparatePdfWithCombined": true
                  }
                }
              ],
              "lastSelectedPresetName": "Legacy PDF"
            }
            """);

        PrintPresetStoreState state = new PrintPresetStorage(storagePath, new TestLogger()).Load();

        PrintPreset preset = Assert.Single(state.Presets);
        Assert.NotNull(preset.Settings);
        Assert.True(preset.Settings!.ExportPdf);
        Assert.True(preset.Settings.CombinePdf);
        Assert.Equal("Legacy PDF", state.LastSelectedPresetName);
    }

    [Fact]
    public void CreateStoragePath_UsesVersionedTrueBimFolder()
    {
        string storagePath = PrintPresetStorage.CreateStoragePath("2025");

        Assert.EndsWith(Path.Combine("TrueBIM", "2025", "print-presets.json"), storagePath);
    }

    private static void LoadRevitApi()
    {
        const string revitApiPath = @"C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll";
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "RevitAPI"))
        {
            return;
        }

        if (File.Exists(revitApiPath))
        {
            Assembly.LoadFrom(revitApiPath);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-tests-" + Guid.NewGuid());
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
