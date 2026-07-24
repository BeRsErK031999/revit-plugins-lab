using System.IO;

namespace TrueBIM.App.Modules.Lintels.Services;

public static class LintelTypeImagePathBuilder
{
    public static string Build(
        string baseDirectory,
        string? projectName,
        string? imageFileName)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Base directory is required.", nameof(baseDirectory));
        }

        string projectToken = SanitizeFileName(projectName, "Несохраненный проект");
        string fileToken = SanitizeFileName(
            Path.GetFileNameWithoutExtension(imageFileName),
            "Перемычка");
        return Path.Combine(
            baseDirectory,
            "TrueBIM",
            "Lintels",
            projectToken,
            $"{fileToken}.png");
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
}
