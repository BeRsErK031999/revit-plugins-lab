using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TrueBIM.Updates;

public sealed class UpdatePackage
{
    public UpdatePackage(string version, string file, string sha256)
    {
        Version = ParseVersion(version);
        if (file != "TrueBIM-Setup-" + version + ".exe")
            throw new InvalidDataException("Некорректное имя установщика в описании обновления.");
        if (!Regex.IsMatch(sha256 ?? "", @"\A[0-9a-fA-F]{64}\z"))
            throw new InvalidDataException("Некорректная контрольная сумма обновления.");
        FileName = file;
        Sha256 = sha256!.ToUpperInvariant();
    }

    public Version Version { get; }
    public string FileName { get; }
    public string Sha256 { get; }

    public static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value ?? "", @"\A\d{1,5}\.\d{1,5}\.\d{1,5}\z")
            || !System.Version.TryParse(value, out Version? version)
            || version.Major > 65535 || version.Minor > 65535 || version.Build > 65535
            || version.ToString(3) != value)
            throw new InvalidDataException("Некорректный номер версии обновления.");
        return version;
    }

    public static string Hash(Stream stream)
    {
        using SHA256 algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
    }

    // Keep this handle open through installer launch/execution so the checked file
    // cannot be replaced or modified between verification and use.
    public FileStream OpenVerifiedInstaller(string path)
    {
        if (Path.GetFileName(path) != FileName)
            throw new InvalidDataException("Имя загруженного установщика не совпадает с обновлением.");
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!string.Equals(Hash(stream), Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма установщика не совпала. Повторите загрузку обновления.");
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            if ((info.ProductVersion ?? "").Trim() != Version.ToString(3)
                || !string.Equals((info.ProductName ?? "").Trim(), "TrueBIM", StringComparison.Ordinal))
                throw new InvalidDataException("Версия или продукт загруженного установщика не совпадает с описанием обновления.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}

public static class UpdateInstallerLaunch
{
    public static string ValidateComponents(string components)
    {
        string[] values = components.Split(',');
        if (values.Length == 0 || values.Distinct(StringComparer.Ordinal).Count() != values.Length
            || values.Any(value => !Regex.IsMatch(value, @"\Arevit20(19|2[0-7])\z")))
            throw new InvalidDataException("Некорректный список версий Revit для обновления.");
        return components;
    }

    public static string QuotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('"') || path.EndsWith("\\", StringComparison.Ordinal)
            || path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            throw new InvalidDataException("Некорректный путь обновления.");
        return "\"" + path + "\"";
    }

    public static string Arguments(string components, string logPath) =>
        "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /TYPE=custom /COMPONENTS="
        + ValidateComponents(components) + " /LOG=" + QuotePath(logPath);

    public static int CountRevitProcesses()
    {
        Process[] processes = Process.GetProcessesByName("Revit");
        try { return processes.Length; }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    public static async Task WaitUntilClosedAsync(Func<int> countProcesses, IProgress<int>? progress,
        CancellationToken cancellationToken, int pollMilliseconds = 1000)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = countProcesses();
            progress?.Report(count);
            if (count == 0) return;
            await Task.Delay(pollMilliseconds, cancellationToken);
        }
    }
}
