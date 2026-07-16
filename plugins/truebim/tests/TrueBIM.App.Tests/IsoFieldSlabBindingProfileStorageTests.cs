using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldSlabBindingProfileStorageTests
{
    [Fact]
    public void Save_RoundTripsProfileForDocumentViewAndSlab()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "profiles.json");
        IsoFieldSlabBindingProfileStorage storage = new(path, new TestLogger());
        IsoFieldSlabBindingProfile profile = CreateProfile("C:\\Models\\A.rvt", 10, 20, 1);

        storage.Save(profile);

        IsoFieldSlabBindingProfile loaded = Assert.IsType<IsoFieldSlabBindingProfile>(
            storage.TryLoad("c:\\models\\a.rvt", 10, 20));
        Assert.Equal("Плита 20", loaded.HostName);
        Assert.Equal(1, loaded.Binding.ImagePoint1.X);
        Assert.Null(storage.TryLoad("C:\\Models\\A.rvt", 11, 20));
        Assert.Null(storage.TryLoad("C:\\Models\\A.rvt", 10, 21));
    }

    [Fact]
    public void Save_ReplacesOnlyMatchingProfile()
    {
        using TempDirectory temp = new();
        IsoFieldSlabBindingProfileStorage storage = new(
            Path.Combine(temp.Path, "profiles.json"),
            new TestLogger());
        storage.Save(CreateProfile("A.rvt", 10, 20, 1));
        storage.Save(CreateProfile("A.rvt", 10, 21, 2));
        storage.Save(CreateProfile("A.rvt", 10, 20, 3));

        Assert.Equal(3, storage.TryLoad("A.rvt", 10, 20)?.Binding.ImagePoint1.X);
        Assert.Equal(2, storage.TryLoad("A.rvt", 10, 21)?.Binding.ImagePoint1.X);
    }

    [Fact]
    public void CreateDocumentKey_UsesTitleForUnsavedDocument()
    {
        string key = IsoFieldSlabBindingProfileStorage.CreateDocumentKey(null, "  Тестовая модель  ");

        Assert.Equal("Тестовая модель", key);
    }

    [Fact]
    public void TryLoad_IgnoresIncompleteProfile()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "profiles.json");
        File.WriteAllText(
            path,
            """
            {
              "SchemaVersion": 1,
              "Profiles": [
                {
                  "DocumentKey": "A.rvt",
                  "ViewId": 10,
                  "HostElementId": 20,
                  "HostName": "Плита",
                  "Binding": null,
                  "SavedAtUtc": "2026-07-16T00:00:00Z"
                }
              ]
            }
            """);
        IsoFieldSlabBindingProfileStorage storage = new(path, new TestLogger());

        Assert.Null(storage.TryLoad("A.rvt", 10, 20));
    }

    private static IsoFieldSlabBindingProfile CreateProfile(
        string documentKey,
        long viewId,
        long hostElementId,
        double seed)
    {
        return new IsoFieldSlabBindingProfile(
            documentKey,
            viewId,
            hostElementId,
            $"Плита {hostElementId}",
            new IsoFieldSlabBindingInput(
                new IsoFieldPoint(seed, 0),
                new IsoFieldPoint(100, 0),
                new IsoFieldPoint(0, 0),
                new IsoFieldPoint(10, 0),
                MirrorImageY: true,
                ImagePoint3: new IsoFieldPoint(0, 100),
                HostPoint3Feet: new IsoFieldPoint(0, 10)),
            DateTimeOffset.UtcNow);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "truebim-isofield-profile-tests-" + Guid.NewGuid());
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
