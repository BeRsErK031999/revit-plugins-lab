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
            ExportDwg = profile.ExportDwg
        };
    }
}
