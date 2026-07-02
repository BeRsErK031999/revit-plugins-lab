using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintCadExportService
{
    public PrintCadExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintCadExportItem> items,
        PrintCadExportFormat format,
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
                bool exported = format == PrintCadExportFormat.Dwg
                    ? document.Export(exportFolder, exportName, viewIds, new DWGExportOptions())
                    : document.Export(exportFolder, exportName, viewIds, new DXFExportOptions());
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
}
