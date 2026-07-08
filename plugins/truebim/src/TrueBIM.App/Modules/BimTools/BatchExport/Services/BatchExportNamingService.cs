using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.BatchExport.Models;
using TrueBIM.App.Modules.Print.Models;
using RevitDocument = Autodesk.Revit.DB.Document;

namespace TrueBIM.App.Modules.BimTools.BatchExport.Services;

public sealed class BatchExportNamingService
{
    public const string DefaultTemplate = "{SheetNumber}_{SheetName}_{Revision}";

    private const int MaxFileNameLength = 180;
    private static readonly Regex TokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public BatchExportFileNamePreview Build(
        string? template,
        PrintSheetInfo sheet,
        BatchExportFileNameContext context,
        int counter)
    {
        Guard.NotNull(sheet, nameof(sheet));
        Guard.NotNull(context, nameof(context));

        string sourceTemplate = string.IsNullOrWhiteSpace(template)
            ? DefaultTemplate
            : template!;
        List<string> missingTokens = new();

        string rawFileName = TokenRegex.Replace(
            sourceTemplate,
            match =>
            {
                string token = match.Groups[1].Value.Trim();
                string? value = ResolveToken(token, sheet, context, counter);
                if (value is not null)
                {
                    return value;
                }

                missingTokens.Add(token);
                return string.Empty;
            });

        string cleanFileName = CleanFileName(rawFileName);
        bool wasTruncated = cleanFileName.Length > MaxFileNameLength;
        if (wasTruncated)
        {
            cleanFileName = cleanFileName.Substring(0, MaxFileNameLength).Trim(' ', '.', '_');
        }

        if (string.IsNullOrWhiteSpace(cleanFileName))
        {
            cleanFileName = $"Лист_{counter.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return new BatchExportFileNamePreview(
            cleanFileName,
            wasTruncated,
            missingTokens.Count > 0,
            missingTokens);
    }

    public static BatchExportFileNameContext CreateContext(RevitDocument document)
    {
        Guard.NotNull(document, nameof(document));

        string documentName = string.IsNullOrWhiteSpace(document.Title)
            ? "Активный документ"
            : document.Title;

        try
        {
            return new BatchExportFileNameContext(
                documentName,
                document.ProjectInformation?.Name ?? string.Empty,
                document.ProjectInformation?.Number ?? string.Empty,
                DateTime.Now,
                CollectProjectParameters(document));
        }
        catch (Exception)
        {
            return new BatchExportFileNameContext(
                documentName,
                string.Empty,
                string.Empty,
                DateTime.Now,
                new Dictionary<string, string>());
        }
    }

    public static string GetInitialExportFolder(RevitDocument document, string? savedFolder)
    {
        if (!string.IsNullOrWhiteSpace(savedFolder))
        {
            return savedFolder!;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                string? documentFolder = Path.GetDirectoryName(document.PathName);
                if (!string.IsNullOrWhiteSpace(documentFolder))
                {
                    return documentFolder;
                }
            }
        }
        catch (ArgumentException)
        {
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string? ResolveToken(
        string token,
        PrintSheetInfo sheet,
        BatchExportFileNameContext context,
        int counter)
    {
        return token switch
        {
            "SheetNumber" => sheet.SheetNumber,
            "SheetName" => sheet.SheetName,
            "ProjectNumber" => context.ProjectNumber,
            "ProjectName" => context.ProjectName,
            "DocumentName" => context.DocumentName,
            "Counter" => counter.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            _ => ResolveSimpleParameterToken(token, sheet, context)
        };
    }

    private static string? ResolveSimpleParameterToken(
        string token,
        PrintSheetInfo sheet,
        BatchExportFileNameContext context)
    {
        if (sheet.SheetParameters.TryGetValue(token, out string? sheetValue))
        {
            return sheetValue;
        }

        return context.ProjectParameters.TryGetValue(token, out string? projectValue)
            ? projectValue
            : null;
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
            ? counter.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : counter.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatDate(string token, DateTime exportDate)
    {
        string format = token.Substring("Date:".Length);
        return string.IsNullOrWhiteSpace(format)
            ? exportDate.ToShortDateString()
            : exportDate.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, string> CollectProjectParameters(RevitDocument document)
    {
        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        if (document.ProjectInformation is null)
        {
            return parameters;
        }

        foreach (Parameter parameter in document.ProjectInformation.Parameters)
        {
            string name = parameter.Definition?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || parameters.ContainsKey(name))
            {
                continue;
            }

            string value = parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }

        return parameters;
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
