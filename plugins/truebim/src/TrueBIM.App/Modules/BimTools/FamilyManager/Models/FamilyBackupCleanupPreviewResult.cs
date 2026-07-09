using System.Globalization;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyBackupCleanupPreviewResult
{
    public FamilyBackupCleanupPreviewResult(
        IReadOnlyList<FamilyBackupFileItem> files,
        IReadOnlyList<string> warnings)
    {
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    public IReadOnlyList<FamilyBackupFileItem> Files { get; }

    public IReadOnlyList<string> Warnings { get; }

    public long TotalSizeBytes => Files.Sum(file => file.SizeBytes);

    public string TotalSizeDisplay => FormatSize(TotalSizeBytes);

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
