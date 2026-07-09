using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyThumbnailCacheService
{
    private const string FileExtension = ".png";

    public FamilyThumbnailCacheService()
        : this(CreateDefaultCacheDirectory())
    {
    }

    public FamilyThumbnailCacheService(string cacheDirectory)
    {
        Guard.NotNullOrWhiteSpace(cacheDirectory, nameof(cacheDirectory));

        CacheDirectory = cacheDirectory;
    }

    public string CacheDirectory { get; }

    public static string CreateDefaultCacheDirectory()
    {
        string settingsPath = JsonSettingsStorage.CreateDefaultSettingsPath("family-manager");
        string? settingsDirectory = Path.GetDirectoryName(settingsPath);
        return Path.Combine(settingsDirectory ?? AppContext.BaseDirectory, "thumbnails");
    }

    public string BuildThumbnailPath(FamilyFileItem family)
    {
        Guard.NotNull(family, nameof(family));

        string key = string.Join(
            "|",
            FamilyPathNormalizer.Normalize(family.FilePath).ToUpperInvariant(),
            family.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            family.LastWriteTimeUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return Path.Combine(CacheDirectory, ComputeSha256Hex(key) + FileExtension);
    }

    public string? TryGetCachedThumbnail(FamilyFileItem family)
    {
        Guard.NotNull(family, nameof(family));

        string expectedPath = BuildThumbnailPath(family);
        if (File.Exists(expectedPath))
        {
            return expectedPath;
        }

        return string.Equals(
                FamilyPathNormalizer.Normalize(family.ThumbnailPath),
                FamilyPathNormalizer.Normalize(expectedPath),
                StringComparison.CurrentCultureIgnoreCase)
            && File.Exists(family.ThumbnailPath)
                ? family.ThumbnailPath
                : null;
    }

    public string Save(FamilyFileItem family, Image thumbnail)
    {
        Guard.NotNull(family, nameof(family));
        Guard.NotNull(thumbnail, nameof(thumbnail));

        Directory.CreateDirectory(CacheDirectory);
        string thumbnailPath = BuildThumbnailPath(family);
        thumbnail.Save(thumbnailPath, ImageFormat.Png);
        return thumbnailPath;
    }

    private static string ComputeSha256Hex(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(bytes);
        StringBuilder builder = new(hash.Length * 2);
        foreach (byte item in hash)
        {
            builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
