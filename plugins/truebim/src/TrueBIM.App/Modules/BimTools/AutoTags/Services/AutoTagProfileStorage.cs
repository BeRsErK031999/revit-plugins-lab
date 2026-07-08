using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Services;

public sealed class AutoTagProfileStorage
{
    private const int DefaultMaxPreviewCount = 500;
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public AutoTagProfileStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath("auto-tags"), logger)
    {
    }

    public AutoTagProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public AutoTagProfile Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, CreateDefaultProfile));
    }

    public void Save(AutoTagProfile profile)
    {
        storage.Save(settingsPath, Normalize(profile));
    }

    public static AutoTagProfile CreateDefaultProfile()
    {
        return new AutoTagProfile
        {
            Name = "Активный вид",
            OnlyUntagged = true,
            UseLeader = false,
            OffsetRightMm = 0,
            OffsetUpMm = 0,
            MaxPreviewCount = DefaultMaxPreviewCount,
            SelectedTagTypeId = null,
            SelectedTagTypeIdsByCategory = [],
            SelectedCategoryIds = []
        };
    }

    public static AutoTagProfile Normalize(AutoTagProfile? profile)
    {
        profile ??= CreateDefaultProfile();

        return new AutoTagProfile
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Активный вид" : profile.Name.Trim(),
            OnlyUntagged = profile.OnlyUntagged,
            UseLeader = profile.UseLeader,
            OffsetRightMm = AutoTagPlacementOffset.NormalizeMillimeters(profile.OffsetRightMm),
            OffsetUpMm = AutoTagPlacementOffset.NormalizeMillimeters(profile.OffsetUpMm),
            MaxPreviewCount = Clamp(profile.MaxPreviewCount <= 0 ? DefaultMaxPreviewCount : profile.MaxPreviewCount, 50, 5000),
            SelectedTagTypeId = profile.SelectedTagTypeId is > 0 ? profile.SelectedTagTypeId : null,
            SelectedTagTypeIdsByCategory = (profile.SelectedTagTypeIdsByCategory ?? [])
                .Where(pair => pair.Value > 0)
                .GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Last().Value),
            SelectedCategoryIds = (profile.SelectedCategoryIds ?? [])
                .Distinct()
                .ToList()
        };
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
