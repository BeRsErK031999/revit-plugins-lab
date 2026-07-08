using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Services;

public sealed class DatumExtentProfileStorage
{
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public DatumExtentProfileStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath("datum-extents"), logger)
    {
    }

    public DatumExtentProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public DatumExtentProfile Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, CreateDefaultProfile));
    }

    public void Save(DatumExtentProfile profile)
    {
        storage.Save(settingsPath, Normalize(profile));
    }

    public static DatumExtentProfile CreateDefaultProfile()
    {
        return new DatumExtentProfile();
    }

    public static DatumExtentProfile Normalize(DatumExtentProfile? profile)
    {
        profile ??= CreateDefaultProfile();
        bool includeEnd0 = profile.IncludeEnd0;
        bool includeEnd1 = profile.IncludeEnd1;
        if (!includeEnd0 && !includeEnd1)
        {
            includeEnd0 = true;
            includeEnd1 = true;
        }

        bool includeGrids = profile.IncludeGrids;
        bool includeLevels = profile.IncludeLevels;
        if (!includeGrids && !includeLevels)
        {
            includeGrids = true;
            includeLevels = true;
        }

        return new DatumExtentProfile
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Активный вид" : profile.Name.Trim(),
            TargetExtentType = DatumExtentTargets.NormalizeProfileValue(profile.TargetExtentType),
            IncludeEnd0 = includeEnd0,
            IncludeEnd1 = includeEnd1,
            IncludeGrids = includeGrids,
            IncludeLevels = includeLevels
        };
    }
}
