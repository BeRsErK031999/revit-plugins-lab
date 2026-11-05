using System.Text.Json;
using TrueBIM.App.Modules.Updates;
using TrueBIM.Updates;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Updates;

public sealed class SharedFolderUpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TrueBIM-update-tests-" + Guid.NewGuid().ToString("N"));
    private string Share => Path.Combine(root, "share");
    private string Cache => Path.Combine(root, "cache");
    private string Source => Path.Combine(Share, "TrueBIM-Setup-99.0.0.exe");
    private string Helper => Path.Combine(root, "TrueBIM.Updater.exe");

    public SharedFolderUpdateTests()
    {
        Directory.CreateDirectory(Share);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "UpdateFixture", "TrueBIM-Setup-99.0.0.exe"), Source);
        File.WriteAllText(Helper, "helper placeholder; preparation tests never execute it");
    }

    private UpdatePackage Package()
    {
        using FileStream stream = File.OpenRead(Source);
        return new UpdatePackage("99.0.0", Path.GetFileName(Source), UpdatePackage.Hash(stream));
    }

    private string Manifest(UpdatePackage package, object? schema = null, string product = "TrueBIM") => JsonSerializer.Serialize(new
    {
        schemaVersion = schema ?? 1, product,
        latest = new { version = package.Version.ToString(3), file = package.FileName, sha256 = package.Sha256 }
    });

    private SharedFolderUpdateService Service() => new(Share, Cache, Helper);

    [Fact]
    public async Task ReadsPublishedVersionAndPreparesVerifiedLocalCopy()
    {
        UpdatePackage package = Package();
        File.WriteAllText(Path.Combine(Share, SharedFolderUpdateService.ManifestFileName), Manifest(package));
        SharedFolderUpdateService service = Service();
        UpdatePackage available = await service.CheckAsync(CancellationToken.None);
        Assert.Equal(package.Sha256, available.Sha256);
        using PreparedUpdate prepared = await service.PrepareAsync(available, null, CancellationToken.None);
        Assert.NotEqual(Source, prepared.InstallerPath);
        Assert.True(File.Exists(prepared.HelperPath));
        using FileStream verified = available.OpenVerifiedInstaller(prepared.InstallerPath);
        Assert.Equal(new FileInfo(Source).Length, verified.Length);
        // Verification holds a read-only lease until the checked executable is used.
        Assert.Throws<IOException>(() => File.Open(prepared.InstallerPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
    }

    [Fact]
    public async Task MissingShareFailsWithoutCreatingAnUpdate()
    {
        SharedFolderUpdateService service = new(Path.Combine(root, "offline"), Cache, Helper);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.CheckAsync(CancellationToken.None));
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public async Task RejectsOversizedManifest()
    {
        File.WriteAllText(Path.Combine(Share, SharedFolderUpdateService.ManifestFileName), new string(' ', 65537));
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().CheckAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(2, "TrueBIM")]
    [InlineData(1, "OtherProduct")]
    public void RejectsUnsupportedReleaseFeed(int schema, string product)
    {
        Assert.Throws<InvalidDataException>(() => SharedFolderUpdateService.ParseManifest(Manifest(Package(), schema, product)));
    }

    [Theory]
    [InlineData("../TrueBIM-Setup-99.0.0.exe")]
    [InlineData("C:\\TrueBIM-Setup-99.0.0.exe")]
    [InlineData("\\\\host\\share\\setup.exe")]
    [InlineData("TrueBIM-Setup-98.0.0.exe")]
    [InlineData("TrueBIM-Setup-99.0.0.exe:payload")]
    public void RejectsUntrustedPackagePaths(string file)
    {
        Assert.Throws<InvalidDataException>(() => new UpdatePackage("99.0.0", file, new string('a', 64)));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3-beta")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3\n")]
    [InlineData("65536.1.0")]
    public void RejectsAmbiguousVersionStrings(string version) => Assert.Throws<InvalidDataException>(() => UpdatePackage.ParseVersion(version));

    [Fact]
    public void ComparesVersionsNumerically()
    {
        Assert.True(UpdatePackage.ParseVersion("0.2.100") > UpdatePackage.ParseVersion("0.2.99"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AA")]
    [InlineData("this-is-not-a-hash")]
    public void RejectsInvalidHash(string hash) => Assert.Throws<InvalidDataException>(() => new UpdatePackage("99.0.0", Path.GetFileName(Source), hash));

    [Fact]
    public async Task RejectsCorruptedCopyAndRemovesIncompleteCache()
    {
        UpdatePackage expected = Package();
        File.AppendAllText(Source, "changed after publication");
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().PrepareAsync(expected, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Cache, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RejectsDifferentExecutableVersionEvenWithMatchingHash()
    {
        string changedName = Path.Combine(Share, "TrueBIM-Setup-98.0.0.exe");
        File.Copy(Source, changedName);
        UpdatePackage mismatched = new("98.0.0", Path.GetFileName(changedName), Package().Sha256);
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().PrepareAsync(mismatched, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Cache, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task NeverQueuesSameOrOlderRelease()
    {
        foreach (string version in new[] { "0.0.1", SharedFolderUpdateService.CurrentVersion.ToString(3) })
        {
            UpdatePackage package = new(version, "TrueBIM-Setup-" + version + ".exe", Package().Sha256);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service().PrepareAsync(package, null, CancellationToken.None));
        }
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public async Task CancelsBeforeCopyWithoutLeavingAQueuedInstaller()
    {
        using CancellationTokenSource token = new();
        token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().PrepareAsync(Package(), null, token.Token));
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public async Task TimeoutAndCancellationReturnWhileAnOsOperationIsStillBlocked()
    {
        TaskCompletionSource<int> blocked = new();
        await Assert.ThrowsAsync<TimeoutException>(() => SharedFolderUpdateService.AwaitWithTimeout(blocked.Task, TimeSpan.FromMilliseconds(20), CancellationToken.None));
        using CancellationTokenSource token = new();
        token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SharedFolderUpdateService.AwaitWithTimeout(blocked.Task, TimeSpan.FromMinutes(1), token.Token));
        blocked.SetException(new IOException("late SMB failure"));
    }

    [Fact]
    public async Task WaitsForEveryRevitProcessAndHonorsCancellation()
    {
        int processes = 2;
        using CancellationTokenSource cancellation = new();
        Task wait = UpdateInstallerLaunch.WaitUntilClosedAsync(() => Volatile.Read(ref processes), null, cancellation.Token, 10);
        await Task.Delay(30);
        Assert.False(wait.IsCompleted);
        Interlocked.Exchange(ref processes, 1);
        await Task.Delay(30);
        Assert.False(wait.IsCompleted);
        Interlocked.Exchange(ref processes, 0);
        await wait;
        processes = 1;
        Task cancelled = UpdateInstallerLaunch.WaitUntilClosedAsync(() => processes, null, cancellation.Token, 10);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }

    [Theory]
    [InlineData("revit2024 /DIR=C:\\Other")]
    [InlineData("revit2024,revit2024")]
    [InlineData("revit2028")]
    [InlineData("")]
    public void RejectsExtraInstallerArguments(string components) => Assert.Throws<InvalidDataException>(() => UpdateInstallerLaunch.Arguments(components, "C:\\Logs\\update.log"));

    [Fact]
    public void InstallerLaunchPreservesSelectedYearsAndNeverClosesOrRestartsApps()
    {
        string arguments = UpdateInstallerLaunch.Arguments("revit2021,revit2024", "C:\\User Files\\installer.log");
        Assert.Contains("/TYPE=custom /COMPONENTS=revit2021,revit2024", arguments);
        Assert.Contains("/NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS", arguments);
        Assert.Contains("/LOG=\"C:\\User Files\\installer.log\"", arguments);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
