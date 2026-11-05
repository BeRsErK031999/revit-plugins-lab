using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using TrueBIM.Updates;

namespace TrueBIM.App.Modules.Updates;

public sealed class SharedFolderUpdateService
{
    public const string ReleaseDirectory = @"W:\Shared\RevitPlugins";
    public const string ManifestFileName = "TrueBIM-release.json";
    public static Version CurrentVersion => new(typeof(SharedFolderUpdateService).Assembly.GetName().Version!.ToString(3));
    private static readonly SemaphoreSlim NetworkOperation = new(1, 1);
    private readonly string releaseDirectory;
    private readonly string cacheDirectory;
    private readonly string helperPath;
    private readonly TimeSpan checkTimeout;
    private readonly TimeSpan copyTimeout;

    public SharedFolderUpdateService(string? releaseDirectory = null, string? cacheDirectory = null,
        string? helperPath = null, TimeSpan? checkTimeout = null, TimeSpan? copyTimeout = null)
    {
        this.releaseDirectory = releaseDirectory ?? ReleaseDirectory;
        this.cacheDirectory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrueBIM", "Updates");
        this.helperPath = helperPath ?? Path.Combine(Path.GetDirectoryName(typeof(SharedFolderUpdateService).Assembly.Location)!, "TrueBIM.Updater.exe");
        this.checkTimeout = checkTimeout ?? TimeSpan.FromSeconds(10);
        this.copyTimeout = copyTimeout ?? TimeSpan.FromMinutes(2);
    }

    public async Task<UpdatePackage> CheckAsync(CancellationToken cancellationToken)
    {
        Task<UpdatePackage> operation = RunNetworkOperation(() =>
        {
            using FileStream stream = new(Path.Combine(releaseDirectory, ManifestFileName), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 64 * 1024) throw new InvalidDataException("Описание обновления слишком большое.");
            using StreamReader reader = new(stream, new UTF8Encoding(false, true));
            return ParseManifest(reader.ReadToEnd());
        }, cancellationToken);
        return await AwaitWithTimeout(operation, checkTimeout, cancellationToken);
    }

    public static UpdatePackage ParseManifest(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("product").GetString() != "TrueBIM")
            throw new InvalidDataException("Файл обновлений не соответствует TrueBIM.");
        JsonElement latest = root.GetProperty("latest");
        return new UpdatePackage(latest.GetProperty("version").GetString()!,
            latest.GetProperty("file").GetString()!, latest.GetProperty("sha256").GetString()!);
    }

    public async Task<PreparedUpdate> PrepareAsync(UpdatePackage package, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (package.Version <= CurrentVersion) throw new InvalidOperationException("Установлена актуальная или более новая версия TrueBIM.");
        PreparedUpdate prepared = new(Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N")), package);
        using CancellationTokenSource copyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<PreparedUpdate> operation = RunNetworkOperation(() =>
        {
            CancellationToken token = copyCancellation.Token;
            try
            {
                Directory.CreateDirectory(prepared.DirectoryPath);
                using (FileStream source = new(Path.Combine(releaseDirectory, package.FileName), FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream target = new(prepared.InstallerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[128 * 1024];
                    long copied = 0, length = source.Length;
                    int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, count);
                        copied += count;
                        progress?.Report(length == 0 ? 0 : (int)(copied * 100 / length));
                    }
                }
                token.ThrowIfCancellationRequested();
                using (package.OpenVerifiedInstaller(prepared.InstallerPath)) { }
                File.Copy(helperPath, prepared.HelperPath);
                // The helper is a standalone .NET Framework 4.8 executable.
                if (File.Exists(helperPath + ".config")) File.Copy(helperPath + ".config", prepared.HelperPath + ".config");
                token.ThrowIfCancellationRequested();
                return prepared;
            }
            catch { prepared.Dispose(); throw; }
        }, copyCancellation.Token);
        try { return await AwaitWithTimeout(operation, copyTimeout, cancellationToken); }
        catch
        {
            copyCancellation.Cancel();
            // A blocked SMB read may outlive the dialog. Clean up only after it has
            // released the files, and never hand a late result to the installer.
            _ = operation.ContinueWith(_ => prepared.Dispose(), TaskScheduler.Default);
            throw;
        }
    }

    public static string InstalledComponents(string revitYear)
    {
        string installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrueBIM");
        string components = string.Join(",", Enumerable.Range(2019, 9).Select(year => year.ToString())
            .Where(year => year == revitYear || File.Exists(Path.Combine(installRoot, year, "TrueBIM.App.dll")))
            .Select(year => "revit" + year));
        return UpdateInstallerLaunch.ValidateComponents(components);
    }

    private static Task<T> RunNetworkOperation<T>(Func<T> action, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NetworkOperation.Wait(0)) throw new IOException("Предыдущая операция с сетевой папкой ещё завершается. Повторите проверку позже.");
        try { return action(); }
        finally { NetworkOperation.Release(); }
    }, cancellationToken);

    public static async Task<T> AwaitWithTimeout<T>(Task<T> operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (await Task.WhenAny(operation, Task.Delay(timeout, timer.Token)) != operation)
        {
            // Observe late failures from an OS file operation that cannot be aborted.
            _ = operation.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Сетевая папка не отвечает. Проверьте подключение к диску W: и повторите попытку.");
        }
        timer.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return await operation;
    }
}

public sealed class PreparedUpdate : IDisposable
{
    private bool handedOff;
    public PreparedUpdate(string directoryPath, UpdatePackage package)
    {
        DirectoryPath = directoryPath;
        Package = package;
    }
    public string DirectoryPath { get; }
    public UpdatePackage Package { get; }
    public string InstallerPath => Path.Combine(DirectoryPath, Package.FileName);
    public string HelperPath => Path.Combine(DirectoryPath, "TrueBIM.Updater.exe");

    public void StartHelper(string components)
    {
        string arguments = UpdateInstallerLaunch.QuotePath(InstallerPath) + " " + Package.Version.ToString(3)
            + " " + Package.Sha256 + " " + UpdateInstallerLaunch.ValidateComponents(components);
        using Process process = Process.Start(new ProcessStartInfo(HelperPath, arguments) { UseShellExecute = false, WorkingDirectory = DirectoryPath })
            ?? throw new IOException("Не удалось запустить помощник обновления.");
        handedOff = true;
    }

    public void Dispose()
    {
        if (handedOff) return;
        try
        {
            File.Delete(InstallerPath);
            File.Delete(HelperPath);
            File.Delete(HelperPath + ".config");
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
