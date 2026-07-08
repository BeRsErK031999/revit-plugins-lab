using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;

public sealed class MepDimensionProfileStorage
{
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public MepDimensionProfileStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath("auto-mep-dimensions"), logger)
    {
    }

    public MepDimensionProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public MepDimensionProfile Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, CreateDefaultProfile));
    }

    public void Save(MepDimensionProfile profile)
    {
        storage.Save(settingsPath, Normalize(profile));
    }

    public static MepDimensionProfile CreateDefaultProfile()
    {
        return new MepDimensionProfile();
    }

    public static MepDimensionProfile Normalize(MepDimensionProfile? profile)
    {
        profile ??= CreateDefaultProfile();
        bool includePipes = profile.IncludePipes;
        bool includeDucts = profile.IncludeDucts;
        bool includeCableTrays = profile.IncludeCableTrays;
        bool includeConduits = profile.IncludeConduits;
        if (!includePipes && !includeDucts && !includeCableTrays && !includeConduits)
        {
            includePipes = true;
        }

        return new MepDimensionProfile
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Активный план" : profile.Name.Trim(),
            IncludePipes = includePipes,
            IncludeDucts = includeDucts,
            IncludeCableTrays = includeCableTrays,
            IncludeConduits = includeConduits,
            AllowElementReferenceFallback = profile.AllowElementReferenceFallback,
            AngleToleranceDegrees = Clamp(profile.AngleToleranceDegrees, 1, 30),
            DimensionLinePlacement = MepDimensionLinePlacements.NormalizeKey(profile.DimensionLinePlacement),
            DimensionOffsetMm = MepDimensionLinePlacements.NormalizeOffsetMillimeters(profile.DimensionOffsetMm)
        };
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 10;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
