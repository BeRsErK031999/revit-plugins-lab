using System.IO;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSlabBindingProfileStorage
{
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public IsoFieldSlabBindingProfileStorage(ITrueBimLogger logger)
        : this(CreateDefaultSettingsPath(), logger)
    {
    }

    public IsoFieldSlabBindingProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("Путь к профилям привязки не задан.", nameof(settingsPath));
        }

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public IsoFieldSlabBindingProfile? TryLoad(
        string documentKey,
        long viewId,
        long hostElementId)
    {
        string normalizedDocumentKey = NormalizeDocumentKey(documentKey, documentKey);
        return LoadCollection().Profiles
            .Where(profile => string.Equals(
                NormalizeDocumentKey(profile.DocumentKey, profile.DocumentKey),
                normalizedDocumentKey,
                StringComparison.OrdinalIgnoreCase))
            .Where(profile => profile.ViewId == viewId && profile.HostElementId == hostElementId)
            .OrderByDescending(profile => profile.SavedAtUtc)
            .FirstOrDefault();
    }

    public void Save(IsoFieldSlabBindingProfile profile)
    {
        IsoFieldSlabBindingProfile normalized = Normalize(profile);
        IsoFieldSlabBindingProfileCollection collection = LoadCollection();
        collection.Profiles.RemoveAll(existing =>
            string.Equals(
                NormalizeDocumentKey(existing.DocumentKey, existing.DocumentKey),
                normalized.DocumentKey,
                StringComparison.OrdinalIgnoreCase)
            && existing.ViewId == normalized.ViewId
            && existing.HostElementId == normalized.HostElementId);
        collection.Profiles.Add(normalized);
        storage.Save(settingsPath, collection);
    }

    public static string CreateDocumentKey(string? documentPath, string? documentTitle)
    {
        return NormalizeDocumentKey(documentPath, documentTitle);
    }

    private IsoFieldSlabBindingProfileCollection LoadCollection()
    {
        IsoFieldSlabBindingProfileCollection collection = storage.LoadOrDefault(
            settingsPath,
            () => new IsoFieldSlabBindingProfileCollection());
        collection.Profiles ??= new List<IsoFieldSlabBindingProfile>();
        collection.Profiles = collection.Profiles
            .Where(IsValidProfile)
            .Select(Normalize)
            .ToList();
        return collection;
    }

    private static IsoFieldSlabBindingProfile Normalize(IsoFieldSlabBindingProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (!IsValidProfile(profile))
        {
            throw new InvalidOperationException("Профиль привязки должен содержать три пары контрольных точек.");
        }

        return profile with
        {
            DocumentKey = NormalizeDocumentKey(profile.DocumentKey, profile.DocumentKey),
            HostName = string.IsNullOrWhiteSpace(profile.HostName)
                ? $"Element {profile.HostElementId}"
                : profile.HostName.Trim(),
            SavedAtUtc = profile.SavedAtUtc == default
                ? DateTimeOffset.UtcNow
                : profile.SavedAtUtc.ToUniversalTime()
        };
    }

    private static bool IsValidProfile(IsoFieldSlabBindingProfile? profile)
    {
        return profile?.Binding is
        {
            ImagePoint1: not null,
            ImagePoint2: not null,
            ImagePoint3: not null,
            HostPoint1Feet: not null,
            HostPoint2Feet: not null,
            HostPoint3Feet: not null
        };
    }

    private static string NormalizeDocumentKey(string? documentPath, string? documentTitle)
    {
        string? candidate = string.IsNullOrWhiteSpace(documentPath)
            ? documentTitle
            : documentPath;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "unsaved-document";
        }

        string trimmed = candidate!.Trim();
        try
        {
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : trimmed;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    private static string CreateDefaultSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrueBIM",
            "IsoFieldRebar",
            "slab-binding-profiles.json");
    }
}
