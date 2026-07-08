using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewProfileStorage
{
    private const string DefaultViewNameTemplate = "BIM_Opening_{CategoryKey}_{ElementId}_{Family}_{Type}";
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public OpeningViewProfileStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath("opening-views"), logger)
    {
    }

    public OpeningViewProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public OpeningViewProfile Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, CreateDefaultProfile));
    }

    public void Save(OpeningViewProfile profile)
    {
        storage.Save(settingsPath, Normalize(profile));
    }

    public static OpeningViewProfile CreateDefaultProfile()
    {
        return new OpeningViewProfile();
    }

    public static OpeningViewProfile Normalize(OpeningViewProfile? profile)
    {
        profile ??= CreateDefaultProfile();
        bool includeDoors = profile.IncludeDoors;
        bool includeWindows = profile.IncludeWindows;
        if (!includeDoors && !includeWindows)
        {
            includeDoors = true;
        }

        return new OpeningViewProfile
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Активный план" : profile.Name.Trim(),
            IncludeDoors = includeDoors,
            IncludeWindows = includeWindows,
            ElevationViewTypeId = NormalizePositiveId(profile.ElevationViewTypeId),
            ViewTemplateId = NormalizePositiveId(profile.ViewTemplateId),
            Scale = Clamp(profile.Scale, 1, 500),
            CropMarginMm = Clamp(profile.CropMarginMm, 0, 5000),
            DepthMarginMm = Clamp(profile.DepthMarginMm, 0, 5000),
            OrientationSource = OpeningViewOrientationSources.NormalizeKey(profile.OrientationSource),
            ViewNameTemplate = string.IsNullOrWhiteSpace(profile.ViewNameTemplate)
                ? DefaultViewNameTemplate
                : profile.ViewNameTemplate.Trim()
        };
    }

    private static long? NormalizePositiveId(long? value)
    {
        return value is > 0 ? value : null;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return min;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
