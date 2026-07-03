using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintPdfExportService
{
    public const string DefaultCombinedPdfFileName = "Объединенный PDF";

    public static PrintPdfExportSettings DefaultSettings { get; } = new(
        PrintPdfColorMode.Color,
        PrintPdfRasterQuality.High,
        AlwaysUseRaster: false);

    public PrintPdfExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintPdfExportItem> items,
        ITrueBimLogger logger)
    {
        return Export(
            document,
            exportFolder,
            items,
            PrintPdfExportMode.SeparateFiles,
            combinedFileName: null,
            DefaultSettings,
            logger);
    }

    public PrintPdfExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintPdfExportItem> items,
        PrintPdfExportMode mode,
        string? combinedFileName,
        ITrueBimLogger logger)
    {
        return Export(
            document,
            exportFolder,
            items,
            mode,
            combinedFileName,
            DefaultSettings,
            logger);
    }

    public PrintPdfExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintPdfExportItem> items,
        PrintPdfExportMode mode,
        string? combinedFileName,
        PrintPdfExportSettings settings,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(exportFolder, nameof(exportFolder));
        Guard.NotNull(items, nameof(items));
        Guard.NotNull(settings, nameof(settings));
        Guard.NotNull(logger, nameof(logger));

#if !REVIT2022_OR_GREATER
        const string unsupportedMessage = "PDF export requires Revit 2022 or newer.";
        logger.Warning(unsupportedMessage);
        return new PrintPdfExportResult(
            new List<string>(),
            items.Select(item => new PrintPdfExportFailure(item, unsupportedMessage)).ToList());
#else
        List<string> exportedFiles = new();
        List<PrintPdfExportFailure> failures = new();

        if (items.Count == 0)
        {
            return new PrintPdfExportResult(exportedFiles, failures);
        }

        try
        {
            Directory.CreateDirectory(exportFolder);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to create PDF export folder.", exception);
            return new PrintPdfExportResult(
                exportedFiles,
                items.Select(item => new PrintPdfExportFailure(item, exception.Message)).ToList());
        }

        return mode switch
        {
            PrintPdfExportMode.SeparateFiles => ExportSeparateFiles(document, exportFolder, items, settings, logger, exportedFiles, failures),
            PrintPdfExportMode.CombinedFile => ExportCombinedFile(document, exportFolder, items, combinedFileName, settings, logger, exportedFiles, failures),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported PDF export mode.")
        };
#endif
    }

#if REVIT2022_OR_GREATER
    private static PrintPdfExportResult ExportSeparateFiles(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintPdfExportItem> items,
        PrintPdfExportSettings settings,
        ITrueBimLogger logger,
        List<string> exportedFiles,
        List<PrintPdfExportFailure> failures)
    {
        foreach (PrintPdfExportItem item in items)
        {
            try
            {
                string pdfFileName = NormalizePdfFileName(item.FileName);
                string outputPath = Path.Combine(exportFolder, pdfFileName);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                using PDFExportOptions options = CreateOptions(pdfFileName, settings);

                List<ElementId> viewIds = new()
                {
                    RevitElementIds.Create(item.ElementId)
                };
                bool exported = document.Export(exportFolder, viewIds, options);
                if (!exported)
                {
                    failures.Add(new PrintPdfExportFailure(item, "Revit не подтвердил экспорт PDF."));
                    continue;
                }

                exportedFiles.Add(outputPath);
                logger.Info($"Exported PDF: {outputPath}. Settings: {GetSettingsDisplayName(settings)}.");
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to export PDF for sheet element id {item.ElementId}.", exception);
                failures.Add(new PrintPdfExportFailure(item, exception.Message));
            }
        }

        return new PrintPdfExportResult(exportedFiles, failures);
    }

    private static PrintPdfExportResult ExportCombinedFile(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintPdfExportItem> items,
        string? combinedFileName,
        PrintPdfExportSettings settings,
        ITrueBimLogger logger,
        List<string> exportedFiles,
        List<PrintPdfExportFailure> failures)
    {
        try
        {
            string pdfFileName = BuildCombinedPdfFileName(combinedFileName);
            string outputPath = Path.Combine(exportFolder, pdfFileName);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            using PDFExportOptions options = CreateOptions(pdfFileName, settings);

            List<ElementId> viewIds = items
                .Select(item => RevitElementIds.Create(item.ElementId))
                .ToList();
            bool exported = document.Export(exportFolder, viewIds, options);
            if (!exported)
            {
                AddCombinedFailure(items, failures, "Revit не подтвердил экспорт объединенного PDF.");
                return new PrintPdfExportResult(exportedFiles, failures);
            }

            exportedFiles.Add(outputPath);
            logger.Info($"Exported combined PDF: {outputPath}. Settings: {GetSettingsDisplayName(settings)}.");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to export combined PDF.", exception);
            AddCombinedFailure(items, failures, exception.Message);
        }

        return new PrintPdfExportResult(exportedFiles, failures);
    }

    private static PDFExportOptions CreateOptions(string fileName, PrintPdfExportSettings settings)
    {
        return new PDFExportOptions
        {
            Combine = true,
            FileName = fileName,
            StopOnError = true,
            ColorDepth = MapColorDepth(settings.ColorMode),
            RasterQuality = MapRasterQuality(settings.RasterQuality),
            AlwaysUseRaster = settings.AlwaysUseRaster
        };
    }

    private static ColorDepthType MapColorDepth(PrintPdfColorMode mode)
    {
        return mode switch
        {
            PrintPdfColorMode.Color => ColorDepthType.Color,
            PrintPdfColorMode.GrayScale => ColorDepthType.GrayScale,
            PrintPdfColorMode.BlackLine => ColorDepthType.BlackLine,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported PDF color mode.")
        };
    }

    private static RasterQualityType MapRasterQuality(PrintPdfRasterQuality quality)
    {
        return quality switch
        {
            PrintPdfRasterQuality.Low => RasterQualityType.Low,
            PrintPdfRasterQuality.Medium => RasterQualityType.Medium,
            PrintPdfRasterQuality.High => RasterQualityType.High,
            PrintPdfRasterQuality.Presentation => RasterQualityType.Presentation,
            _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unsupported PDF raster quality.")
        };
    }
#endif

    public static string NormalizePdfFileName(string fileName)
    {
        Guard.NotNullOrWhiteSpace(fileName, nameof(fileName));

        string normalizedFileName = Path.GetFileName(fileName.Trim());
        return string.Equals(Path.GetExtension(normalizedFileName), ".pdf", StringComparison.OrdinalIgnoreCase)
            ? normalizedFileName
            : normalizedFileName + ".pdf";
    }

    public static string BuildCombinedPdfFileName(string? fileName)
    {
        string sourceFileName = string.IsNullOrWhiteSpace(fileName)
            ? DefaultCombinedPdfFileName
            : fileName!;
        string normalizedFileName = NormalizePdfFileName(sourceFileName);
        string baseName = Path.GetFileNameWithoutExtension(normalizedFileName);
        string cleanBaseName = CleanFileNameBase(baseName);

        return string.IsNullOrWhiteSpace(cleanBaseName)
            ? DefaultCombinedPdfFileName + ".pdf"
            : cleanBaseName + ".pdf";
    }

    public static string GetModeDisplayName(PrintPdfExportMode mode)
    {
        return mode switch
        {
            PrintPdfExportMode.SeparateFiles => "отдельные PDF",
            PrintPdfExportMode.CombinedFile => "один PDF",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported PDF export mode.")
        };
    }

    public static string GetSettingsDisplayName(PrintPdfExportSettings settings)
    {
        Guard.NotNull(settings, nameof(settings));

        string vectorMode = settings.AlwaysUseRaster ? "растр" : "вектор";
        return $"{GetColorModeDisplayName(settings.ColorMode)}, {GetRasterQualityDisplayName(settings.RasterQuality)}, {vectorMode}";
    }

    public static string GetColorModeDisplayName(PrintPdfColorMode mode)
    {
        return mode switch
        {
            PrintPdfColorMode.Color => "цвет",
            PrintPdfColorMode.GrayScale => "оттенки серого",
            PrintPdfColorMode.BlackLine => "черные линии",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported PDF color mode.")
        };
    }

    public static string GetRasterQualityDisplayName(PrintPdfRasterQuality quality)
    {
        return quality switch
        {
            PrintPdfRasterQuality.Low => "низкое качество",
            PrintPdfRasterQuality.Medium => "среднее качество",
            PrintPdfRasterQuality.High => "высокое качество",
            PrintPdfRasterQuality.Presentation => "презентационное качество",
            _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unsupported PDF raster quality.")
        };
    }

    private static string CleanFileNameBase(string fileName)
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

    private static void AddCombinedFailure(
        IReadOnlyList<PrintPdfExportItem> items,
        List<PrintPdfExportFailure> failures,
        string message)
    {
        failures.AddRange(items.Select(item => new PrintPdfExportFailure(item, message)));
    }
}
