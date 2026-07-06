using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintFileNameTemplateService
{
    public const string DefaultTemplate = "{SheetNumber}_{SheetName}";

    private const int MaxFileNameLength = 180;
    private static readonly Regex TokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public PrintFileNamePreview Build(
        string? template,
        PrintSheetInfo sheet,
        PrintFileNameContext context,
        int counter)
    {
        Guard.NotNull(sheet, nameof(sheet));
        Guard.NotNull(context, nameof(context));

        string sourceTemplate = string.IsNullOrWhiteSpace(template)
            ? DefaultTemplate
            : template!;
        bool hasUnknownTokens = false;

        string rawFileName = TokenRegex.Replace(
            sourceTemplate,
            match =>
            {
                string token = match.Groups[1].Value.Trim();
                string? value = ResolveToken(token, sheet, context, counter);
                if (value is null)
                {
                    hasUnknownTokens = true;
                    return string.Empty;
                }

                return value;
            });

        string cleanFileName = CleanFileName(rawFileName);
        bool wasTruncated = cleanFileName.Length > MaxFileNameLength;
        if (wasTruncated)
        {
            cleanFileName = cleanFileName.Substring(0, MaxFileNameLength).Trim(' ', '.', '_');
        }

        if (string.IsNullOrWhiteSpace(cleanFileName))
        {
            cleanFileName = "Лист";
        }

        return new PrintFileNamePreview(cleanFileName, wasTruncated, hasUnknownTokens);
    }

    private static string? ResolveToken(
        string token,
        PrintSheetInfo sheet,
        PrintFileNameContext context,
        int counter)
    {
        return token switch
        {
            "SheetNumber" => sheet.SheetNumber,
            "SheetName" => sheet.SheetName,
            "ProjectNumber" => context.ProjectNumber,
            "ProjectName" => context.ProjectName,
            "DocumentName" => context.DocumentName,
            "Counter" => counter.ToString(CultureInfo.InvariantCulture),
            _ when token.StartsWith("Counter:", StringComparison.Ordinal) => FormatCounter(token, counter),
            _ when token.StartsWith("Date:", StringComparison.Ordinal) => FormatDate(token, context.ExportDate),
            _ when token.StartsWith("SheetParameter:", StringComparison.Ordinal) => ResolveDictionaryToken(
                sheet.SheetParameters,
                token,
                "SheetParameter:"),
            _ when token.StartsWith("ProjectParameter:", StringComparison.Ordinal) => ResolveDictionaryToken(
                context.ProjectParameters,
                token,
                "ProjectParameter:"),
            _ => null
        };
    }

    private static string? ResolveDictionaryToken(
        IReadOnlyDictionary<string, string> values,
        string token,
        string prefix)
    {
        string key = token.Substring(prefix.Length).Trim();
        return !string.IsNullOrWhiteSpace(key) && values.TryGetValue(key, out string? value)
            ? value
            : null;
    }

    private static string FormatCounter(string token, int counter)
    {
        string format = token.Substring("Counter:".Length);
        return string.IsNullOrWhiteSpace(format)
            ? counter.ToString(CultureInfo.InvariantCulture)
            : counter.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatDate(string token, DateTime exportDate)
    {
        string format = token.Substring("Date:".Length);
        return string.IsNullOrWhiteSpace(format)
            ? exportDate.ToShortDateString()
            : exportDate.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string CleanFileName(string fileName)
    {
        string cleanFileName = fileName;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            cleanFileName = cleanFileName.Replace(invalidChar, '_');
        }

        cleanFileName = cleanFileName.Trim(' ', '.', '_');
        while (cleanFileName.IndexOf("__", StringComparison.Ordinal) >= 0)
        {
            cleanFileName = cleanFileName.Replace("__", "_");
        }

        return cleanFileName.Trim(' ', '.', '_');
    }
}
