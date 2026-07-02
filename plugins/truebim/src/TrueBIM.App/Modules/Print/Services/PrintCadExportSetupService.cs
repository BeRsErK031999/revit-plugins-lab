using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintCadExportSetupService
{
    public const string DefaultDisplayName = "По умолчанию";
    public const string DefaultWithoutSetupsDisplayName = "По умолчанию (настроек нет)";

    public IReadOnlyList<PrintCadExportSetupOption> GetAvailableOptions(
        Document document,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(logger, nameof(logger));

        try
        {
            return BuildSetupOptions(BaseExportOptions.GetPredefinedSetupNames(document));
        }
        catch (Exception exception)
        {
            logger.Error("Failed to read CAD export setups from Revit document.", exception);
            return BuildSetupOptions(Array.Empty<string>());
        }
    }

    public BaseExportOptions CreateOptions(
        Document document,
        PrintCadExportFormat format,
        string? setupName,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(logger, nameof(logger));

        string? normalizedSetupName = NormalizeSetupName(setupName);
        if (normalizedSetupName is null)
        {
            logger.Info($"Using default {GetFormatDisplayName(format)} export options.");
            return CreateDefaultOptions(format);
        }

        try
        {
            BaseExportOptions options = format switch
            {
                PrintCadExportFormat.Dwg => DWGExportOptions.GetPredefinedOptions(document, normalizedSetupName),
                PrintCadExportFormat.Dxf => DXFExportOptions.GetPredefinedOptions(document, normalizedSetupName),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
            };
            logger.Info($"Using {GetFormatDisplayName(format)} export setup '{normalizedSetupName}'.");
            return options;
        }
        catch (Exception exception)
        {
            logger.Error(
                $"Failed to load {GetFormatDisplayName(format)} export setup '{normalizedSetupName}'. Using default options.",
                exception);
            return CreateDefaultOptions(format);
        }
    }

    public static IReadOnlyList<PrintCadExportSetupOption> BuildSetupOptions(IEnumerable<string>? setupNames)
    {
        List<string> normalizedSetupNames = new();
        HashSet<string> seenSetupNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (string? setupName in setupNames ?? Array.Empty<string>())
        {
            string? normalizedSetupName = NormalizeSetupName(setupName);
            if (normalizedSetupName is null || !seenSetupNames.Add(normalizedSetupName))
            {
                continue;
            }

            normalizedSetupNames.Add(normalizedSetupName);
        }

        List<PrintCadExportSetupOption> options = new()
        {
            new PrintCadExportSetupOption(
                null,
                normalizedSetupNames.Count == 0 ? DefaultWithoutSetupsDisplayName : DefaultDisplayName)
        };
        options.AddRange(normalizedSetupNames.Select(name => new PrintCadExportSetupOption(name, name)));

        return options;
    }

    public static string? NormalizeSetupName(string? setupName)
    {
        if (string.IsNullOrWhiteSpace(setupName))
        {
            return null;
        }

        return setupName!.Trim();
    }

    public static string GetSelectionDisplayName(
        PrintCadExportFormat format,
        PrintCadExportSetupOption? option)
    {
        return $"{GetFormatDisplayName(format)}: {option?.DisplayName ?? DefaultDisplayName}";
    }

    public static BaseExportOptions CreateDefaultOptions(PrintCadExportFormat format)
    {
        return format switch
        {
            PrintCadExportFormat.Dwg => new DWGExportOptions(),
            PrintCadExportFormat.Dxf => new DXFExportOptions(),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
        };
    }

    private static string GetFormatDisplayName(PrintCadExportFormat format)
    {
        return format switch
        {
            PrintCadExportFormat.Dwg => "DWG",
            PrintCadExportFormat.Dxf => "DXF",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
        };
    }
}
