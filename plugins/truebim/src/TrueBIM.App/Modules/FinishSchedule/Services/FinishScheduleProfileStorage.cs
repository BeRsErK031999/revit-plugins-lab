using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishScheduleProfileStorage
{
    public const string SettingsKey = "finish-schedule";
    private const int CurrentVersion = 2;

    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public FinishScheduleProfileStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath(SettingsKey), logger)
    {
    }

    public FinishScheduleProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("Settings path must not be empty.", nameof(settingsPath));
        }

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public FinishScheduleSettings Load()
    {
        FinishScheduleProfileData profile = storage.LoadOrDefault(
            settingsPath,
            FinishScheduleProfileData.CreateDefault);
        return ToSettings(profile);
    }

    public void Save(FinishScheduleSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        storage.Save(settingsPath, FromSettings(settings));
    }

    internal static FinishScheduleSettings ToSettings(FinishScheduleProfileData? profile)
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        profile ??= FinishScheduleProfileData.CreateDefault();

        RoomIdentifierMode roomMode = IsDefined(profile.RoomIdentifierMode)
            ? profile.RoomIdentifierMode!.Value
            : defaults.RoomIdentifier.Mode;
        ParameterReference? roomParameter = roomMode == RoomIdentifierMode.CustomParameter
            ? profile.RoomIdentifierParameter?.ToReference()
            : null;

        ReportScopeKind scopeKind = IsDefined(profile.ScopeKind)
            ? profile.ScopeKind!.Value
            : defaults.Scope.Kind;
        long? levelId = scopeKind == ReportScopeKind.Level && profile.LevelId is > 0
            ? profile.LevelId
            : null;
        ParameterReference? sectionParameter = scopeKind == ReportScopeKind.Section
            ? profile.SectionParameter?.ToReference()
            : null;
        string sectionValue = scopeKind == ReportScopeKind.Section
            ? profile.SectionValue?.Trim() ?? string.Empty
            : string.Empty;

        return defaults with
        {
            DescriptionParameter = profile.DescriptionParameter?.ToReference(),
            RoomIdentifier = new RoomIdentifierSettings(roomMode, roomParameter),
            WriteOwnership = profile.WriteOwnership ?? defaults.WriteOwnership,
            Walls = ToCategorySettings(profile.Walls, defaults.Walls),
            Floors = ToCategorySettings(profile.Floors, defaults.Floors),
            Ceilings = ToCategorySettings(profile.Ceilings, defaults.Ceilings),
            RoomListOutputParameter = profile.RoomListOutputParameter?.ToReference(),
            Scope = new ReportScopeSettings(scopeKind, levelId, sectionParameter, sectionValue),
            ScheduleName = NormalizeText(profile.ScheduleName, defaults.ScheduleName),
            ColumnWidths = new FinishScheduleColumnWidths(
                NormalizeWidth(
                    profile.RoomListColumnWidthMillimeters,
                    defaults.EffectiveColumnWidths.RoomListMillimeters),
                NormalizeWidth(
                    profile.DescriptionColumnWidthMillimeters,
                    defaults.EffectiveColumnWidths.DescriptionMillimeters),
                NormalizeWidth(
                    profile.AreaColumnWidthMillimeters,
                    defaults.EffectiveColumnWidths.AreaMillimeters))
        };
    }

    private static FinishScheduleProfileData FromSettings(FinishScheduleSettings settings)
    {
        return new FinishScheduleProfileData
        {
            Version = CurrentVersion,
            DescriptionParameter = ParameterReferenceData.FromReference(settings.DescriptionParameter),
            RoomIdentifierMode = settings.RoomIdentifier.Mode,
            RoomIdentifierParameter = ParameterReferenceData.FromReference(settings.RoomIdentifier.CustomParameter),
            WriteOwnership = settings.WriteOwnership,
            Walls = FinishCategoryProfileData.FromSettings(settings.Walls),
            Floors = FinishCategoryProfileData.FromSettings(settings.Floors),
            Ceilings = FinishCategoryProfileData.FromSettings(settings.Ceilings),
            RoomListOutputParameter = ParameterReferenceData.FromReference(settings.RoomListOutputParameter),
            ScopeKind = settings.Scope.Kind,
            LevelId = settings.Scope.LevelId,
            SectionParameter = ParameterReferenceData.FromReference(settings.Scope.SectionParameter),
            SectionValue = settings.Scope.SectionValue,
            ScheduleName = settings.ScheduleName,
            RoomListColumnWidthMillimeters = settings.EffectiveColumnWidths.RoomListMillimeters,
            DescriptionColumnWidthMillimeters = settings.EffectiveColumnWidths.DescriptionMillimeters,
            AreaColumnWidthMillimeters = settings.EffectiveColumnWidths.AreaMillimeters
        };
    }

    private static FinishCategorySettings ToCategorySettings(
        FinishCategoryProfileData? profile,
        FinishCategorySettings defaults)
    {
        if (profile is null)
        {
            return defaults;
        }

        return new FinishCategorySettings(
            profile.IsEnabled ?? defaults.IsEnabled,
            NormalizeText(profile.ClassificationValue, defaults.ClassificationValue),
            profile.OwnershipParameter?.ToReference(),
            profile.OutputDescriptionParameter?.ToReference(),
            profile.OutputAreaParameter?.ToReference());
    }

    private static string NormalizeText(string? value, string fallback)
    {
        return value is null ? fallback : value.Trim();
    }

    private static double NormalizeWidth(double? value, double fallback)
    {
        return value is >= FinishScheduleColumnWidths.MinimumMillimeters
            and <= FinishScheduleColumnWidths.MaximumMillimeters
            && !double.IsNaN(value.Value)
            && !double.IsInfinity(value.Value)
                ? value.Value
                : fallback;
    }

    private static bool IsDefined<TEnum>(TEnum? value)
        where TEnum : struct, Enum
    {
        return value.HasValue && Enum.IsDefined(typeof(TEnum), value.Value);
    }
}

internal sealed class FinishScheduleProfileData
{
    public int Version { get; set; }

    public ParameterReferenceData? DescriptionParameter { get; set; }

    public RoomIdentifierMode? RoomIdentifierMode { get; set; }

    public ParameterReferenceData? RoomIdentifierParameter { get; set; }

    public bool? WriteOwnership { get; set; }

    public FinishCategoryProfileData? Walls { get; set; }

    public FinishCategoryProfileData? Floors { get; set; }

    public FinishCategoryProfileData? Ceilings { get; set; }

    public ParameterReferenceData? RoomListOutputParameter { get; set; }

    public ReportScopeKind? ScopeKind { get; set; }

    public long? LevelId { get; set; }

    public ParameterReferenceData? SectionParameter { get; set; }

    public string? SectionValue { get; set; }

    public string? ScheduleName { get; set; }

    public double? RoomListColumnWidthMillimeters { get; set; }

    public double? DescriptionColumnWidthMillimeters { get; set; }

    public double? AreaColumnWidthMillimeters { get; set; }

    public static FinishScheduleProfileData CreateDefault()
    {
        return new FinishScheduleProfileData { Version = CurrentProfileVersion };
    }

    private const int CurrentProfileVersion = 2;
}

internal sealed class FinishCategoryProfileData
{
    public bool? IsEnabled { get; set; }

    public string? ClassificationValue { get; set; }

    public ParameterReferenceData? OwnershipParameter { get; set; }

    public ParameterReferenceData? OutputDescriptionParameter { get; set; }

    public ParameterReferenceData? OutputAreaParameter { get; set; }

    public static FinishCategoryProfileData FromSettings(FinishCategorySettings settings)
    {
        return new FinishCategoryProfileData
        {
            IsEnabled = settings.IsEnabled,
            ClassificationValue = settings.ClassificationValue,
            OwnershipParameter = ParameterReferenceData.FromReference(settings.OwnershipParameter),
            OutputDescriptionParameter = ParameterReferenceData.FromReference(settings.OutputDescriptionParameter),
            OutputAreaParameter = ParameterReferenceData.FromReference(settings.OutputAreaParameter)
        };
    }
}

internal sealed class ParameterReferenceData
{
    public string? Name { get; set; }

    public ParameterIdentityKind IdentityKind { get; set; }

    public long? BuiltInParameterId { get; set; }

    public Guid? SharedParameterGuid { get; set; }

    public long? DefinitionElementId { get; set; }

    public ParameterBindingKind BindingKind { get; set; }

    public ParameterStorageKind StorageKind { get; set; }

    public ParameterReference? ToReference()
    {
        if (string.IsNullOrWhiteSpace(Name)
            || !Enum.IsDefined(typeof(ParameterIdentityKind), IdentityKind)
            || !Enum.IsDefined(typeof(ParameterBindingKind), BindingKind)
            || !Enum.IsDefined(typeof(ParameterStorageKind), StorageKind))
        {
            return null;
        }

        string name = Name!;
        try
        {
            return IdentityKind switch
            {
                ParameterIdentityKind.BuiltIn when BuiltInParameterId.HasValue => ParameterReference.BuiltIn(
                    name,
                    BuiltInParameterId.Value,
                    BindingKind,
                    StorageKind,
                    DefinitionElementId),
                ParameterIdentityKind.Shared when SharedParameterGuid.HasValue && DefinitionElementId.HasValue => ParameterReference.Shared(
                    name,
                    SharedParameterGuid.Value,
                    DefinitionElementId.Value,
                    BindingKind,
                    StorageKind),
                ParameterIdentityKind.Project when DefinitionElementId.HasValue => ParameterReference.Project(
                    name,
                    DefinitionElementId.Value,
                    BindingKind,
                    StorageKind),
                _ => null
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static ParameterReferenceData? FromReference(ParameterReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        return new ParameterReferenceData
        {
            Name = reference.Name,
            IdentityKind = reference.IdentityKind,
            BuiltInParameterId = reference.BuiltInParameterId,
            SharedParameterGuid = reference.SharedParameterGuid,
            DefinitionElementId = reference.DefinitionElementId,
            BindingKind = reference.BindingKind,
            StorageKind = reference.StorageKind
        };
    }
}
