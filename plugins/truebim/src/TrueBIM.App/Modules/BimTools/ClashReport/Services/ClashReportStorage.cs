using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashReportStorage
{
    private const string SettingsKey = "clash-report";
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;
    private ClashReportSettings settings;

    public ClashReportStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath(SettingsKey), logger)
    {
    }

    public ClashReportStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
        this.settingsPath = settingsPath;
        settings = Normalize(storage.LoadOrDefault(settingsPath, () => new ClashReportSettings()));
    }

    public ClashReportProfile LoadProfile()
    {
        settings = Normalize(storage.LoadOrDefault(settingsPath, () => new ClashReportSettings()));
        return Clone(settings.Profile);
    }

    public void SaveProfile(ClashReportProfile profile)
    {
        settings.Profile = NormalizeProfile(profile);
        Save();
    }

    public void ApplyState(string modelKey, ClashItem item)
    {
        Guard.NotNullOrWhiteSpace(modelKey, nameof(modelKey));
        Guard.NotNull(item, nameof(item));

        if (!settings.States.TryGetValue(BuildStateKey(modelKey, item.StableId), out ClashStateRecord? record))
        {
            return;
        }

        item.Status = record.Status;
        item.Comment = record.Comment;
    }

    public void SaveStates(string modelKey, IEnumerable<ClashItem> items, ClashReportProfile profile)
    {
        Guard.NotNullOrWhiteSpace(modelKey, nameof(modelKey));
        Guard.NotNull(items, nameof(items));

        settings.Profile = NormalizeProfile(profile);
        foreach (ClashItem item in items)
        {
            settings.States[BuildStateKey(modelKey, item.StableId)] = new ClashStateRecord
            {
                Status = item.Status,
                Comment = item.Comment,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        Save();
    }

    public static string BuildModelKey(Document document)
    {
        Guard.NotNull(document, nameof(document));

        try
        {
            string? projectInfoId = document.ProjectInformation?.UniqueId;
            if (!string.IsNullOrWhiteSpace(projectInfoId))
            {
                return projectInfoId!;
            }
        }
        catch (InvalidOperationException)
        {
        }

        if (!string.IsNullOrWhiteSpace(document.PathName))
        {
            return document.PathName;
        }

        return string.IsNullOrWhiteSpace(document.Title)
            ? "Untitled"
            : document.Title;
    }

    public static string BuildStateKey(string modelKey, string clashId)
    {
        Guard.NotNullOrWhiteSpace(modelKey, nameof(modelKey));
        Guard.NotNullOrWhiteSpace(clashId, nameof(clashId));

        return $"{modelKey}::{clashId.Trim()}";
    }

    public static ClashReportSettings Normalize(ClashReportSettings? value)
    {
        ClashReportSettings settings = value ?? new ClashReportSettings();
        settings.Profile = NormalizeProfile(settings.Profile);
        settings.States ??= new Dictionary<string, ClashStateRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (ClashStateRecord record in settings.States.Values)
        {
            record.Comment = record.Comment?.Trim() ?? string.Empty;
        }

        return settings;
    }

    public static ClashReportProfile NormalizeProfile(ClashReportProfile? profile)
    {
        ClashReportProfile source = profile ?? new ClashReportProfile();
        return new ClashReportProfile
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Импорт коллизий" : source.Name.Trim(),
            LastImportPath = source.LastImportPath?.Trim() ?? string.Empty,
            HighlightOnNavigate = source.HighlightOnNavigate
        };
    }

    private void Save()
    {
        storage.Save(settingsPath, settings);
    }

    private static ClashReportProfile Clone(ClashReportProfile profile)
    {
        return new ClashReportProfile
        {
            Name = profile.Name,
            LastImportPath = profile.LastImportPath,
            HighlightOnNavigate = profile.HighlightOnNavigate
        };
    }
}
