using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintFileNameTemplateService
{
    public const string DefaultTemplate = "{Номер листа}_{Имя листа}";
    public const string DefaultCombinedTemplate = "{Имя документа}";

    private const int MaxFileNameLength = 180;
    private static readonly Regex TokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public IReadOnlyCollection<string> GetSheetParameterNames(params string?[] templates)
    {
        return GetDictionaryTokenNames(
            templates,
            "SheetParameter:",
            "Параметр листа:");
    }

    public IReadOnlyCollection<string> GetProjectParameterNames(params string?[] templates)
    {
        return GetDictionaryTokenNames(
            templates,
            "ProjectParameter:",
            "Параметр проекта:");
    }

    public IReadOnlyCollection<string> GetTitleBlockParameterNames(params string?[] templates)
    {
        return GetDictionaryTokenNames(
            templates,
            "TitleBlockParameter:",
            "Параметр основной надписи:");
    }

    public PrintFileNamePreview BuildCombined(
        string? template,
        IReadOnlyList<PrintSheetInfo> sheets,
        PrintFileNameContext context)
    {
        Guard.NotNull(sheets, nameof(sheets));
        if (sheets.Count == 0)
        {
            throw new ArgumentException("At least one sheet is required to build a combined file name.", nameof(sheets));
        }

        string sourceTemplate = string.IsNullOrWhiteSpace(template)
            ? DefaultCombinedTemplate
            : template!;
        return Build(sourceTemplate, sheets[0], context, counter: 1);
    }

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
            "SheetNumber" or "НомерЛиста" or "Номер листа" => sheet.SheetNumber,
            "SheetName" or "ИмяЛиста" or "Имя листа" => sheet.SheetName,
            "ProjectNumber" or "НомерПроекта" or "Номер проекта" => context.ProjectNumber,
            "ProjectName" or "ИмяПроекта" or "Имя проекта" => context.ProjectName,
            "DocumentName" or "ИмяДокумента" or "Имя документа" => context.DocumentName,
            "Counter" or "Счетчик" or "Счётчик" => counter.ToString(CultureInfo.InvariantCulture),
            _ when token.StartsWith("Counter:", StringComparison.Ordinal) => FormatCounter(token, "Counter:", counter),
            _ when token.StartsWith("Счетчик:", StringComparison.CurrentCultureIgnoreCase) => FormatCounter(token, "Счетчик:", counter),
            _ when token.StartsWith("Счётчик:", StringComparison.CurrentCultureIgnoreCase) => FormatCounter(token, "Счётчик:", counter),
            _ when token.StartsWith("Date:", StringComparison.Ordinal) => FormatDate(token, "Date:", context.ExportDate),
            _ when token.StartsWith("Дата:", StringComparison.CurrentCultureIgnoreCase) => FormatDate(token, "Дата:", context.ExportDate),
            _ when token.StartsWith("SheetParameter:", StringComparison.Ordinal) => ResolveDictionaryToken(
                sheet.SheetParameters,
                token,
                "SheetParameter:"),
            _ when token.StartsWith("Параметр листа:", StringComparison.CurrentCultureIgnoreCase) => ResolveDictionaryToken(
                sheet.SheetParameters,
                token,
                "Параметр листа:"),
            _ when token.StartsWith("TitleBlockParameter:", StringComparison.Ordinal) => ResolveDictionaryToken(
                sheet.TitleBlockParameters,
                token,
                "TitleBlockParameter:"),
            _ when token.StartsWith("Параметр основной надписи:", StringComparison.CurrentCultureIgnoreCase) => ResolveDictionaryToken(
                sheet.TitleBlockParameters,
                token,
                "Параметр основной надписи:"),
            _ when token.StartsWith("ProjectParameter:", StringComparison.Ordinal) => ResolveDictionaryToken(
                context.ProjectParameters,
                token,
                "ProjectParameter:"),
            _ when token.StartsWith("Параметр проекта:", StringComparison.CurrentCultureIgnoreCase) => ResolveDictionaryToken(
                context.ProjectParameters,
                token,
                "Параметр проекта:"),
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

    private static IReadOnlyCollection<string> GetDictionaryTokenNames(
        IReadOnlyCollection<string?> templates,
        string englishPrefix,
        string russianPrefix)
    {
        Guard.NotNull(templates, nameof(templates));

        HashSet<string> parameterNames = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (string? template in templates)
        {
            string sourceTemplate = string.IsNullOrWhiteSpace(template)
                ? DefaultTemplate
                : template!;
            foreach (Match match in TokenRegex.Matches(sourceTemplate))
            {
                string token = match.Groups[1].Value.Trim();
                string? parameterName = GetDictionaryTokenName(token, englishPrefix, StringComparison.Ordinal)
                    ?? GetDictionaryTokenName(token, russianPrefix, StringComparison.CurrentCultureIgnoreCase);
                if (!string.IsNullOrWhiteSpace(parameterName))
                {
                    parameterNames.Add(parameterName!);
                }
            }
        }

        return parameterNames;
    }

    private static string? GetDictionaryTokenName(
        string token,
        string prefix,
        StringComparison comparison)
    {
        return token.StartsWith(prefix, comparison)
            ? token.Substring(prefix.Length).Trim()
            : null;
    }

    private static string FormatCounter(string token, string prefix, int counter)
    {
        string format = token.Substring(prefix.Length);
        return string.IsNullOrWhiteSpace(format)
            ? counter.ToString(CultureInfo.InvariantCulture)
            : counter.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatDate(string token, string prefix, DateTime exportDate)
    {
        string format = token.Substring(prefix.Length);
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
