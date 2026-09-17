using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TrueBIM.App.Modules.Lintels.Services;

public static class LintelTypeImagePathBuilder
{
    private const int MaxWindowsPathLength = 240;
    private const int MaxProjectTokenLength = 64;
    private const int MaxFileTokenLength = 96;
    private const int MinimumFileTokenLength = 24;

    public static string Build(
        string baseDirectory,
        string? projectName,
        string? imageFileName)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Base directory is required.", nameof(baseDirectory));
        }

        string rootDirectory = Path.Combine(baseDirectory, "TrueBIM", "Lintels");
        int availableProjectLength = MaxWindowsPathLength
            - rootDirectory.Length
            - MinimumFileTokenLength
            - ".png".Length
            - 2;
        if (availableProjectLength < 12)
        {
            throw new InvalidOperationException(
                "Базовый каталог PNG слишком длинный для безопасного экспорта Revit.");
        }

        string projectToken = ShortenToken(
            SanitizeFileName(projectName, "Несохраненный проект"),
            Math.Min(MaxProjectTokenLength, availableProjectLength));
        string outputDirectory = Path.Combine(rootDirectory, projectToken);
        int availableFileTokenLength = MaxWindowsPathLength
            - outputDirectory.Length
            - ".png".Length
            - 1;
        string fileToken = ShortenToken(SanitizeFileName(
            Path.GetFileNameWithoutExtension(imageFileName),
            "Перемычка"), Math.Min(MaxFileTokenLength, availableFileTokenLength));
        return Path.Combine(outputDirectory, $"{fileToken}.png");
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        HashSet<char> invalidCharacters = new(Path.GetInvalidFileNameChars());
        string normalized = new string((value ?? string.Empty)
            .Trim()
            .Select(character => invalidCharacters.Contains(character) || char.IsControl(character)
                ? '_'
                : character)
            .ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized;
    }

    private static string ShortenToken(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        const int hashLength = 8;
        if (maxLength <= hashLength + 1)
        {
            throw new InvalidOperationException(
                "Недостаточно места для безопасного имени PNG перемычки.");
        }

        byte[] hashBytes;
        using (SHA256 hash = SHA256.Create())
        {
            hashBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        string hashToken = BitConverter.ToString(hashBytes, 0, hashLength / 2)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        int prefixLength = maxLength - hashLength - 1;
        return $"{value.Substring(0, prefixLength).TrimEnd(' ', '.', '_')}_{hashToken}";
    }
}
