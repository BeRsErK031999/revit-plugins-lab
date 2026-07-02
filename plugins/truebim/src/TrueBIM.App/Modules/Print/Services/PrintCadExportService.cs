using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintCadExportService
{
    private readonly PrintCadExportSetupService setupService;

    public PrintCadExportService()
        : this(new PrintCadExportSetupService())
    {
    }

    internal PrintCadExportService(PrintCadExportSetupService setupService)
    {
        this.setupService = setupService ?? throw new ArgumentNullException(nameof(setupService));
    }

    public PrintCadExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintCadExportItem> items,
        PrintCadExportFormat format,
        string? setupName,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(exportFolder, nameof(exportFolder));
        Guard.NotNull(items, nameof(items));
        Guard.NotNull(logger, nameof(logger));

        List<string> exportedFiles = new();
        List<PrintCadExportFailure> failures = new();

        try
        {
            Directory.CreateDirectory(exportFolder);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to create CAD export folder.", exception);
            return new PrintCadExportResult(
                format,
                exportedFiles,
                items.Select(item => new PrintCadExportFailure(format, item, exception.Message)).ToList());
        }

        BaseExportOptions options = setupService.CreateOptions(document, format, setupName, logger);
        foreach (PrintCadExportItem item in items)
        {
            try
            {
                string cadFileName = NormalizeCadFileName(item.FileName, format);
                string exportName = Path.GetFileNameWithoutExtension(cadFileName);
                string outputPath = Path.Combine(exportFolder, cadFileName);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                List<ElementId> viewIds = new()
                {
                    RevitElementIds.Create(item.ElementId)
                };
                bool exported = ExportWithOptions(document, exportFolder, exportName, viewIds, format, options);
                if (!exported)
                {
                    failures.Add(new PrintCadExportFailure(format, item, "Revit не подтвердил экспорт CAD."));
                    continue;
                }

                exportedFiles.Add(outputPath);
                logger.Info($"Exported {GetDisplayName(format)}: {outputPath}");
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to export {GetDisplayName(format)} for sheet element id {item.ElementId}.", exception);
                failures.Add(new PrintCadExportFailure(format, item, exception.Message));
            }
        }

        return new PrintCadExportResult(format, exportedFiles, failures);
    }

    public static string NormalizeCadFileName(string fileName, PrintCadExportFormat format)
    {
        Guard.NotNullOrWhiteSpace(fileName, nameof(fileName));

        string extension = GetExtension(format);
        string normalizedFileName = Path.GetFileName(fileName.Trim());
        return string.Equals(Path.GetExtension(normalizedFileName), extension, StringComparison.OrdinalIgnoreCase)
            ? normalizedFileName
            : normalizedFileName + extension;
    }

    public static string GetDisplayName(PrintCadExportFormat format)
    {
        return format switch
        {
            PrintCadExportFormat.Dwg => "DWG",
            PrintCadExportFormat.Dxf => "DXF",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
        };
    }

    private static string GetExtension(PrintCadExportFormat format)
    {
        return format switch
        {
            PrintCadExportFormat.Dwg => ".dwg",
            PrintCadExportFormat.Dxf => ".dxf",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
        };
    }

    private static bool ExportWithOptions(
        Document document,
        string exportFolder,
        string exportName,
        ICollection<ElementId> viewIds,
        PrintCadExportFormat format,
        BaseExportOptions options)
    {
        return format switch
        {
            PrintCadExportFormat.Dwg => document.Export(exportFolder, exportName, viewIds, (DWGExportOptions)options),
            PrintCadExportFormat.Dxf => document.Export(exportFolder, exportName, viewIds, (DXFExportOptions)options),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
        };
    }
}
