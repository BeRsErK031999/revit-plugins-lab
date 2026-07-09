using System.Reflection;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class DwgExportProfileStorageTests
{
    public DwgExportProfileStorageTests()
    {
        LoadRevitApi();
    }

    [Fact]
    public void Load_ReturnsEmptyStateWhenProfileFileDoesNotExist()
    {
        using TempDirectory temp = new();
        DwgExportProfileStorage storage = new(Path.Combine(temp.Path, "dwg-export-profiles.json"), new TestLogger());

        Assert.False(storage.StorageFileExists);
        DwgExportProfileStoreState state = storage.Load();

        Assert.Empty(state.Profiles);
        Assert.Null(state.LastSelectedProfileName);
    }

    [Fact]
    public void Save_PersistsNormalizedProfilesAndLastState()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "nested", "dwg-export-profiles.json");
        DwgExportProfileStorage storage = new(storagePath, new TestLogger());

        DwgExportProfile profile = new()
        {
            ProfileName = "  Office DWG  ",
            SourceRevitSetupName = "  Revit Office Setup  ",
            IsUserProfile = true,
            LayerMapping = "  AIA  ",
            LinetypesFileName = "  C:\\CAD\\office.lin  ",
            HatchPatternsFileName = "  C:\\CAD\\office.pat  ",
            NonplotSuffix = "  -NPLT  ",
            HatchBackgroundColor = new DwgExportColor(12, 34, 56)
        };

        DwgExportProfileStoreState state = new()
        {
            Profiles = [profile],
            LastSelectedProfileName = "  Office DWG  ",
            LastFolder = "  C:\\Exports  ",
            LastNameMask = "  {SheetNumber}_{SheetName}  ",
            LastFormatSelection = "  DWG  "
        };

        storage.Save(state);

        Assert.True(storage.StorageFileExists);
        DwgExportProfileStoreState reloadedState = new DwgExportProfileStorage(storagePath, new TestLogger()).Load();
        DwgExportProfile reloadedProfile = Assert.Single(reloadedState.Profiles);
        Assert.Equal("Office DWG", reloadedProfile.ProfileName);
        Assert.Equal("Revit Office Setup", reloadedProfile.SourceRevitSetupName);
        Assert.Equal("AIA", reloadedProfile.LayerMapping);
        Assert.Equal("C:\\CAD\\office.lin", reloadedProfile.LinetypesFileName);
        Assert.Equal("C:\\CAD\\office.pat", reloadedProfile.HatchPatternsFileName);
        Assert.Equal("-NPLT", reloadedProfile.NonplotSuffix);
        Assert.Equal(new DwgExportColor(12, 34, 56), reloadedProfile.HatchBackgroundColor);
        Assert.Equal("Office DWG", reloadedState.LastSelectedProfileName);
        Assert.Equal("C:\\Exports", reloadedState.LastFolder);
        Assert.Equal("{SheetNumber}_{SheetName}", reloadedState.LastNameMask);
        Assert.Equal("DWG", reloadedState.LastFormatSelection);
    }

    [Fact]
    public void Normalize_DeduplicatesProfilesAndRepairsMissingLastSelection()
    {
        DwgExportProfileStoreState state = new()
        {
            Profiles =
            [
                new DwgExportProfile { ProfileName = "Office DWG" },
                new DwgExportProfile { ProfileName = " office dwg " },
                new DwgExportProfile { ProfileName = "Detail DWG" }
            ],
            LastSelectedProfileName = "Missing"
        };

        DwgExportProfileStoreState normalizedState = DwgExportProfileStorage.Normalize(state);

        Assert.Collection(
            normalizedState.Profiles,
            profile => Assert.Equal("Office DWG", profile.ProfileName),
            profile => Assert.Equal("Detail DWG", profile.ProfileName));
        Assert.Equal("Office DWG", normalizedState.LastSelectedProfileName);
    }

    [Fact]
    public void CreateStoragePath_UsesVersionedTrueBimFolder()
    {
        string storagePath = DwgExportProfileStorage.CreateStoragePath("2025");

        Assert.EndsWith(Path.Combine("TrueBIM", "2025", "dwg-export-profiles.json"), storagePath);
    }

    [Theory]
    [InlineData("#0C2238", 12, 34, 56)]
    [InlineData("0C2238", 12, 34, 56)]
    public void DwgExportColor_TryParse_ReadsHexColors(string value, byte red, byte green, byte blue)
    {
        Assert.True(DwgExportColor.TryParse(value, out DwgExportColor color));
        Assert.Equal(new DwgExportColor(red, green, blue), color);
        Assert.Equal("#0C2238", color.ToHex());
    }

    [Fact]
    public void DwgExportColor_TryParse_RejectsInvalidColors()
    {
        Assert.False(DwgExportColor.TryParse("not-a-color", out _));
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
}
