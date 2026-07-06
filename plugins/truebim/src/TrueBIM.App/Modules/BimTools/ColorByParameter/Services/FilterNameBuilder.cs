using System.Text;
using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Services;

public sealed class FilterNameBuilder
{
    public const string Prefix = "BIM_F_";
    public const int MaxLength = 96;

    public string Build(string parameterName, string valueDisplay)
    {
        string safeParameterName = Sanitize(parameterName, "Параметр");
        string safeValue = Sanitize(valueDisplay, "Пусто");
        string hash = ComputeHash($"{parameterName}|{valueDisplay}");
        string suffix = $"_{hash}";
        string name = $"{Prefix}{safeParameterName}_{safeValue}";

        if (name.Length + suffix.Length <= MaxLength)
        {
            return name + suffix;
        }

        int availableLength = MaxLength - suffix.Length;
        if (availableLength <= Prefix.Length)
        {
            return Prefix + hash;
        }

        return name.Substring(0, availableLength).TrimEnd('_', ' ') + suffix;
    }

    public bool IsOwnedFilterName(string? name)
    {
        string filterName = name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return false;
        }

        return filterName.StartsWith(Prefix, StringComparison.Ordinal);
    }

    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder builder = new(value.Length);
        foreach (char character in value.Trim())
        {
            builder.Append(IsUnsafe(character) ? '_' : character);
        }

        string result = Regex.Replace(builder.ToString(), "\\s+", " ");
        result = Regex.Replace(result, "_+", "_").Trim(' ', '_');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static bool IsUnsafe(char character)
    {
        return char.IsControl(character)
            || character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '[' or ']' or '{' or '}';
    }

    private static string ComputeHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
