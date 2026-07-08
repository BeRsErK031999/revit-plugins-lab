using System.Text;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewNameService
{
    private const int MaxNameLength = 120;
    private const string DefaultTemplate = "BIM_Opening_{CategoryKey}_{ElementId}_{Family}_{Type}";
    private static readonly char[] InvalidCharacters =
    [
        '\\', '/', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~'
    ];

    public static string Build(string? template, OpeningViewNameContext context)
    {
        context = Guard.NotNull(context, nameof(context));

        string value = template is null || string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template.Trim();
        value = ReplaceToken(value, "{ElementId}", context.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        value = ReplaceToken(value, "{CategoryKey}", context.CategoryKey);
        value = ReplaceToken(value, "{Category}", context.CategoryName);
        value = ReplaceToken(value, "{Family}", context.Family);
        value = ReplaceToken(value, "{Type}", context.Type);
        value = ReplaceToken(value, "{Level}", context.Level);

        string sanitized = Sanitize(value);
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"BIM_Opening_{context.ElementId}"
            : sanitized;
    }

    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        bool lastWasSpace = false;
        foreach (char character in value.Trim())
        {
            char normalized = InvalidCharacters.Contains(character) || char.IsControl(character)
                ? '_'
                : character;
            if (char.IsWhiteSpace(normalized))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(normalized);
            lastWasSpace = false;
        }

        string sanitized = builder.ToString().Trim(' ', '.', '_');
        return sanitized.Length <= MaxNameLength
            ? sanitized
            : sanitized.Substring(0, MaxNameLength).Trim(' ', '.', '_');
    }

    private static string ReplaceToken(string value, string token, string replacement)
    {
        int index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            value = value.Remove(index, token.Length).Insert(index, replacement);
            index = value.IndexOf(token, index + replacement.Length, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }
}
