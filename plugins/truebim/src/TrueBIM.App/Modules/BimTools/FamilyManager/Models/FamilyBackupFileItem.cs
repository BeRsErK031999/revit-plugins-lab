using System.Globalization;
using System.IO;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyBackupFileItem
{
    public string FilePath { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;

    public string PrimaryFilePath { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public int BackupIndex { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public string BackupIndexDisplay => BackupIndex.ToString("0000", CultureInfo.InvariantCulture);

    public string SizeDisplay => FormatSize(SizeBytes);

    public string LastWriteDisplay => LastWriteTimeUtc == default
        ? "-"
        : LastWriteTimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

    private static string FormatSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return "0 KB";
        }

        double sizeKb = sizeBytes / 1024d;
        if (sizeKb < 1024)
        {
            return $"{Math.Round(sizeKb, 1).ToString("0.0", CultureInfo.InvariantCulture)} KB";
        }

        return $"{Math.Round(sizeKb / 1024d, 2).ToString("0.00", CultureInfo.InvariantCulture)} MB";
    }
}
