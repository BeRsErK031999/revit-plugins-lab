using TrueBIM.App.Modules.BimTools.BatchExport.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.BatchExport.Services;

public sealed class BatchExportProfileStorage
{
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public BatchExportProfileStorage(ITrueBimLogger logger)
        : this(
            JsonSettingsStorage.CreateDefaultSettingsPath("batch-export"),
            logger)
    {
    }

    public BatchExportProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public BatchExportProfile Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, () => new BatchExportProfile()));
    }

    public void Save(BatchExportProfile profile)
    {
        storage.Save(settingsPath, Normalize(profile));
    }

    public static BatchExportProfile Normalize(BatchExportProfile? profile)
    {
        profile ??= new BatchExportProfile();
        string? exportFolder = profile.ExportFolder;

        return new BatchExportProfile
        {
            Name = string.IsNullOrWhiteSpace(profile.Name)
                ? "Рабочая документация"
                : profile.Name.Trim(),
            ExportFolder = string.IsNullOrWhiteSpace(exportFolder)
                ? null
                : exportFolder!.Trim(),
            FileNameTemplate = string.IsNullOrWhiteSpace(profile.FileNameTemplate)
                ? BatchExportNamingService.DefaultTemplate
                : profile.FileNameTemplate.Trim(),
            ExportPdf = profile.ExportPdf,
            ExportDwg = profile.ExportDwg,
            ActiveSheetSetName = NormalizeActiveSheetSetName(profile.ActiveSheetSetName, profile.SheetSets),
            SheetSets = NormalizeSheetSets(profile.SheetSets)
        };
    }

    private static string? NormalizeActiveSheetSetName(string? activeSheetSetName, IEnumerable<BatchExportSheetSet>? sheetSets)
    {
        if (string.IsNullOrWhiteSpace(activeSheetSetName))
        {
            return null;
        }

        string normalizedName = (activeSheetSetName ?? string.Empty).Trim();
        return NormalizeSheetSets(sheetSets).Any(sheetSet => string.Equals(sheetSet.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase))
            ? normalizedName
            : null;
    }

    private static List<BatchExportSheetSet> NormalizeSheetSets(IEnumerable<BatchExportSheetSet>? sheetSets)
    {
        Dictionary<string, BatchExportSheetSet> normalized = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (BatchExportSheetSet sheetSet in sheetSets ?? [])
        {
            if (string.IsNullOrWhiteSpace(sheetSet.Name))
            {
                continue;
            }

            string name = sheetSet.Name.Trim();
            List<string> sheetNumbers = (sheetSet.SheetNumbers ?? [])
                .Where(sheetNumber => !string.IsNullOrWhiteSpace(sheetNumber))
                .Select(sheetNumber => sheetNumber.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(sheetNumber => sheetNumber, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (sheetNumbers.Count == 0)
            {
                continue;
            }

            if (!normalized.TryGetValue(name, out BatchExportSheetSet? existing))
            {
                normalized[name] = new BatchExportSheetSet
                {
                    Name = name,
                    SheetNumbers = sheetNumbers
                };
                continue;
            }

            existing.SheetNumbers = existing.SheetNumbers
                .Concat(sheetNumbers)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(sheetNumber => sheetNumber, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return normalized.Values
            .OrderBy(sheetSet => sheetSet.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
