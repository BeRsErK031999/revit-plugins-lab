using System.Reflection;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class DwgExportOptionsFactory
{
    public DWGExportOptions Create(Document document, DwgExportProfile profile, ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(profile, nameof(profile));
        Guard.NotNull(logger, nameof(logger));

        DwgExportProfile normalizedProfile = DwgExportProfileStorage.NormalizeProfile(profile);
        DWGExportOptions options = CreateBaseOptions(document, normalizedProfile, logger);
        ApplyProfile(options, normalizedProfile, logger);
        logger.Info($"DWG export profile applied: {GetProfileSummary(normalizedProfile)}.");
        return options;
    }

    public DwgExportProfile CreateProfile(
        Document document,
        string? setupName,
        string profileName,
        bool isUserProfile,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(logger, nameof(logger));

        string? normalizedSetupName = PrintCadExportSetupService.NormalizeSetupName(setupName);
        DWGExportOptions options = new();
        if (normalizedSetupName is not null)
        {
            try
            {
                options = DWGExportOptions.GetPredefinedOptions(document, normalizedSetupName);
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to load DWG export setup '{normalizedSetupName}' for profile.", exception);
            }
        }

        return CreateProfileFromOptions(
            string.IsNullOrWhiteSpace(profileName) ? normalizedSetupName ?? DwgExportProfile.DefaultProfileName : profileName,
            normalizedSetupName,
            usePredefinedRevitSetup: normalizedSetupName is not null,
            isUserProfile,
            options);
    }

    public string CreateRevitExportSetup(Document document, DwgExportProfile profile, ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(profile, nameof(profile));
        Guard.NotNull(logger, nameof(logger));

        DwgExportProfile normalizedProfile = DwgExportProfileStorage.NormalizeProfile(profile);
        DWGExportOptions options = Create(document, normalizedProfile, logger);
        string setupName = BuildUniqueSetupName(document, normalizedProfile.ProfileName);

        using Transaction transaction = new(document, "TrueBIM: create DWG export setup");
        transaction.Start();
        ExportDWGSettings.Create(document, setupName, options);
        transaction.Commit();

        logger.Info($"Created Revit DWG export setup '{setupName}' from TrueBIM profile '{normalizedProfile.ProfileName}'.");
        return setupName;
    }

    public static DwgExportProfile CreateProfileFromOptions(
        string profileName,
        string? sourceRevitSetupName,
        bool usePredefinedRevitSetup,
        bool isUserProfile,
        DWGExportOptions options)
    {
        Guard.NotNull(options, nameof(options));

        return new DwgExportProfile
        {
            ProfileName = DwgExportProfileStorage.NormalizeProfileName(profileName),
            SourceRevitSetupName = PrintCadExportSetupService.NormalizeSetupName(sourceRevitSetupName),
            UsePredefinedRevitSetup = usePredefinedRevitSetup,
            IsUserProfile = isUserProfile,
            FileVersion = GetOption(options, "FileVersion", default(ACADVersion)),
            Colors = GetOption(options, "Colors", default(ExportColorMode)),
            PropOverrides = GetOption(options, "PropOverrides", default(PropOverrideMode)),
            TargetUnit = GetOption(options, "TargetUnit", default(ExportUnit)),
            SharedCoords = GetOption(options, "SharedCoords", default(bool)),
            ExportingAreas = GetOption(options, "ExportingAreas", default(bool)),
            MergedViews = GetOption(options, "MergedViews", default(bool)),
            HideScopeBox = GetOption(options, "HideScopeBox", default(bool)),
            HideReferencePlane = GetOption(options, "HideReferencePlane", default(bool)),
            HideUnreferenceViewTags = GetOption(options, "HideUnreferenceViewTags", default(bool)),
            PreserveCoincidentLines = GetOption(options, "PreserveCoincidentLines", default(bool)),
            ExportOfSolids = GetOption(options, "ExportOfSolids", default(SolidGeometry)),
            LineScaling = GetOption(options, "LineScaling", default(LineScaling)),
            TextTreatment = GetOption(options, "TextTreatment", default(TextTreatment)),
            LayerMapping = GetOption<string?>(options, "LayerMapping", null),
            LinetypesFileName = GetOption<string?>(options, "LinetypesFileName", null),
            HatchPatternsFileName = GetOption<string?>(options, "HatchPatternsFileName", null),
            MarkNonplotLayers = GetOption(options, "MarkNonplotLayers", default(bool)),
            NonplotSuffix = GetOption<string?>(options, "NonplotSuffix", null),
            UseHatchBackgroundColor = GetOption(options, "UseHatchBackgroundColor", default(bool)),
            HatchBackgroundColor = GetColorOption(options, "HatchBackgroundColor", DwgExportColor.White)
        };
    }

    public static string GetProfileSummary(DwgExportProfile profile)
    {
        Guard.NotNull(profile, nameof(profile));

        string setup = profile.UsePredefinedRevitSetup && !string.IsNullOrWhiteSpace(profile.SourceRevitSetupName)
            ? profile.SourceRevitSetupName!
            : "default DWG options";
        string source = profile.IsUserProfile ? "пользовательский профиль" : "Revit Export Setup";
        string coordinates = profile.SharedCoords ? "shared coordinates" : "project/internal coordinates";
        return $"{profile.ProfileName} ({source}; base: {setup}; version: {profile.FileVersion}; colors: {profile.Colors}; units: {profile.TargetUnit}; coordinates: {coordinates}; solids: {profile.ExportOfSolids})";
    }

    private static DWGExportOptions CreateBaseOptions(
        Document document,
        DwgExportProfile profile,
        ITrueBimLogger logger)
    {
        if (!profile.UsePredefinedRevitSetup || string.IsNullOrWhiteSpace(profile.SourceRevitSetupName))
        {
            logger.Info("Using default DWG export options as profile base.");
            return new DWGExportOptions();
        }

        try
        {
            logger.Info($"Using Revit DWG export setup '{profile.SourceRevitSetupName}' as profile base.");
            return DWGExportOptions.GetPredefinedOptions(document, profile.SourceRevitSetupName);
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to load DWG export setup '{profile.SourceRevitSetupName}'. Using default DWG options.", exception);
            return new DWGExportOptions();
        }
    }

    private static void ApplyProfile(
        DWGExportOptions options,
        DwgExportProfile profile,
        ITrueBimLogger logger)
    {
        SetOption(options, "FileVersion", profile.FileVersion, logger);
        SetOption(options, "Colors", profile.Colors, logger);
        SetOption(options, "PropOverrides", profile.PropOverrides, logger);
        SetOption(options, "TargetUnit", profile.TargetUnit, logger);
        SetOption(options, "SharedCoords", profile.SharedCoords, logger);
        SetOption(options, "ExportingAreas", profile.ExportingAreas, logger);
        SetOption(options, "MergedViews", profile.MergedViews, logger);
        SetOption(options, "HideScopeBox", profile.HideScopeBox, logger);
        SetOption(options, "HideReferencePlane", profile.HideReferencePlane, logger);
        SetOption(options, "HideUnreferenceViewTags", profile.HideUnreferenceViewTags, logger);
        SetOption(options, "PreserveCoincidentLines", profile.PreserveCoincidentLines, logger);
        SetOption(options, "ExportOfSolids", profile.ExportOfSolids, logger);
        SetOption(options, "LineScaling", profile.LineScaling, logger);
        SetOption(options, "TextTreatment", profile.TextTreatment, logger);
        SetOption(options, "LayerMapping", profile.LayerMapping ?? string.Empty, logger);
        SetOption(options, "LinetypesFileName", profile.LinetypesFileName ?? string.Empty, logger);
        SetOption(options, "HatchPatternsFileName", profile.HatchPatternsFileName ?? string.Empty, logger);
        SetOption(options, "MarkNonplotLayers", profile.MarkNonplotLayers, logger);
        SetOption(options, "NonplotSuffix", profile.NonplotSuffix ?? string.Empty, logger);
        SetOption(options, "UseHatchBackgroundColor", profile.UseHatchBackgroundColor, logger);
        SetOption(options, "HatchBackgroundColor", new Color(
            profile.HatchBackgroundColor.Red,
            profile.HatchBackgroundColor.Green,
            profile.HatchBackgroundColor.Blue), logger);
    }

    private static void SetOption<T>(DWGExportOptions options, string propertyName, T value, ITrueBimLogger logger)
    {
        PropertyInfo? property = options.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite != true)
        {
            logger.Info($"DWG option '{propertyName}' is not available in this Revit API version.");
            return;
        }

        try
        {
            property.SetValue(options, value);
        }
        catch (Exception exception) when (exception is ArgumentException or TargetInvocationException or MethodAccessException)
        {
            logger.Warning($"Failed to apply DWG option '{propertyName}': {exception.Message}");
        }
    }

    private static T GetOption<T>(DWGExportOptions options, string propertyName, T fallback)
    {
        PropertyInfo? property = options.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanRead != true)
        {
            return fallback;
        }

        try
        {
            object? value = property.GetValue(options);
            return value is T typedValue ? typedValue : fallback;
        }
        catch (Exception exception) when (exception is TargetInvocationException or MethodAccessException)
        {
            return fallback;
        }
    }

    private static DwgExportColor GetColorOption(DWGExportOptions options, string propertyName, DwgExportColor fallback)
    {
        Color color = GetOption(options, propertyName, new Color(fallback.Red, fallback.Green, fallback.Blue));
        return new DwgExportColor(color.Red, color.Green, color.Blue);
    }

    private static string BuildUniqueSetupName(Document document, string requestedName)
    {
        string baseName = DwgExportProfileStorage.NormalizeProfileName(requestedName);
        HashSet<string> existingNames = BaseExportOptions
            .GetPredefinedSetupNames(document)
            .Select(PrintCadExportSetupService.NormalizeSetupName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        int suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }
        while (existingNames.Contains(candidate));

        return candidate;
    }
}
