using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintCadExportService
{
    private readonly PrintCadExportSetupService setupService;
    private readonly DwgExportOptionsFactory dwgOptionsFactory;

    public PrintCadExportService()
        : this(new PrintCadExportSetupService(), new DwgExportOptionsFactory())
    {
    }

    internal PrintCadExportService(PrintCadExportSetupService setupService)
        : this(setupService, new DwgExportOptionsFactory())
    {
    }

    internal PrintCadExportService(PrintCadExportSetupService setupService, DwgExportOptionsFactory dwgOptionsFactory)
    {
        this.setupService = setupService ?? throw new ArgumentNullException(nameof(setupService));
        this.dwgOptionsFactory = dwgOptionsFactory ?? throw new ArgumentNullException(nameof(dwgOptionsFactory));
    }

    public PrintCadExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintCadExportItem> items,
        PrintCadExportFormat format,
        string? setupName,
        ITrueBimLogger logger)
    {
        return Export(document, exportFolder, items, format, setupName, mergeViews: false, mergedFileName: null, logger);
    }

    public PrintCadExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintCadExportItem> items,
        PrintCadExportFormat format,
        string? setupName,
        bool mergeViews,
        string? mergedFileName,
        ITrueBimLogger logger)
    {
        return Export(document, exportFolder, items, format, setupName, dwgProfile: null, mergeViews, mergedFileName, logger);
    }

    public PrintCadExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintCadExportItem> items,
        PrintCadExportFormat format,
        string? setupName,
        DwgExportProfile? dwgProfile,
        bool mergeViews,
        string? mergedFileName,
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

        if (format == PrintCadExportFormat.Dwf)
        {
            return ExportDwf(document, exportFolder, items, mergeViews, mergedFileName, logger, exportedFiles, failures);
        }

        BaseExportOptions options = CreateExportOptions(document, format, setupName, dwgProfile, logger);
        if (mergeViews && format == PrintCadExportFormat.Dwg)
        {
            return ExportMergedDwg(document, exportFolder, items, mergedFileName, options, logger, exportedFiles, failures);
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

    private BaseExportOptions CreateExportOptions(
        Document document,
        PrintCadExportFormat format,
        string? setupName,
        DwgExportProfile? dwgProfile,
        ITrueBimLogger logger)
    {
        if (format == PrintCadExportFormat.Dwg && dwgProfile is not null)
        {
            return dwgOptionsFactory.Create(document, dwgProfile, logger);
        }

        return setupService.CreateOptions(document, format, setupName, logger);
    }

    private static PrintCadExportResult ExportMergedDwg(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintCadExportItem> items,
        string? mergedFileName,
        BaseExportOptions options,
        ITrueBimLogger logger,
        List<string> exportedFiles,
        List<PrintCadExportFailure> failures)
    {
        try
        {
            string cadFileName = NormalizeCadFileName(
                string.IsNullOrWhiteSpace(mergedFileName) ? "Объединенный DWG" : mergedFileName!,
                PrintCadExportFormat.Dwg);
            string exportName = Path.GetFileNameWithoutExtension(cadFileName);
            string outputPath = Path.Combine(exportFolder, cadFileName);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            if (options is DWGExportOptions dwgOptions)
            {
                dwgOptions.MergedViews = true;
            }

            List<ElementId> viewIds = items
                .Select(item => RevitElementIds.Create(item.ElementId))
                .ToList();
            bool exported = ExportWithOptions(document, exportFolder, exportName, viewIds, PrintCadExportFormat.Dwg, options);
            if (!exported)
            {
                AddFailureForItems(PrintCadExportFormat.Dwg, items, failures, "Revit не подтвердил экспорт объединенного DWG.");
                return new PrintCadExportResult(PrintCadExportFormat.Dwg, exportedFiles, failures);
            }

            exportedFiles.Add(outputPath);
            logger.Info($"Exported merged DWG: {outputPath}");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to export merged DWG.", exception);
            AddFailureForItems(PrintCadExportFormat.Dwg, items, failures, exception.Message);
        }

        return new PrintCadExportResult(PrintCadExportFormat.Dwg, exportedFiles, failures);
    }

    private static PrintCadExportResult ExportDwf(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintCadExportItem> items,
        bool mergeViews,
        string? mergedFileName,
        ITrueBimLogger logger,
        List<string> exportedFiles,
        List<PrintCadExportFailure> failures)
    {
        if (mergeViews)
        {
            try
            {
                string dwfFileName = NormalizeCadFileName(
                    string.IsNullOrWhiteSpace(mergedFileName) ? "Объединенный DWF" : mergedFileName!,
                    PrintCadExportFormat.Dwf);
                string outputPath = Path.Combine(exportFolder, dwfFileName);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                ViewSet viewSet = CreateViewSet(document, items);
                DWFExportOptions options = new()
                {
                    MergedViews = true,
                    StopOnError = true
                };
                bool exported = document.Export(exportFolder, Path.GetFileNameWithoutExtension(dwfFileName), viewSet, options);
                if (!exported)
                {
                    AddFailureForItems(PrintCadExportFormat.Dwf, items, failures, "Revit не подтвердил экспорт объединенного DWF.");
                    return new PrintCadExportResult(PrintCadExportFormat.Dwf, exportedFiles, failures);
                }

                exportedFiles.Add(outputPath);
                logger.Info($"Exported merged DWF: {outputPath}");
            }
            catch (Exception exception)
            {
                logger.Error("Failed to export merged DWF.", exception);
                AddFailureForItems(PrintCadExportFormat.Dwf, items, failures, exception.Message);
            }

            return new PrintCadExportResult(PrintCadExportFormat.Dwf, exportedFiles, failures);
        }

        foreach (PrintCadExportItem item in items)
        {
            try
            {
                string dwfFileName = NormalizeCadFileName(item.FileName, PrintCadExportFormat.Dwf);
                string outputPath = Path.Combine(exportFolder, dwfFileName);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                ViewSet viewSet = CreateViewSet(document, [item]);
                DWFExportOptions options = new()
                {
                    MergedViews = false,
                    StopOnError = true
                };
                bool exported = document.Export(exportFolder, Path.GetFileNameWithoutExtension(dwfFileName), viewSet, options);
                if (!exported)
                {
                    failures.Add(new PrintCadExportFailure(PrintCadExportFormat.Dwf, item, "Revit не подтвердил экспорт DWF."));
                    continue;
                }

                exportedFiles.Add(outputPath);
                logger.Info($"Exported DWF: {outputPath}");
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to export DWF for sheet element id {item.ElementId}.", exception);
                failures.Add(new PrintCadExportFailure(PrintCadExportFormat.Dwf, item, exception.Message));
            }
        }

        return new PrintCadExportResult(PrintCadExportFormat.Dwf, exportedFiles, failures);
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
            PrintCadExportFormat.Dwf => "DWF",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
        };
    }

    private static string GetExtension(PrintCadExportFormat format)
    {
        return format switch
        {
            PrintCadExportFormat.Dwg => ".dwg",
            PrintCadExportFormat.Dxf => ".dxf",
            PrintCadExportFormat.Dwf => ".dwf",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported CAD export format.")
        };
    }

    private static ViewSet CreateViewSet(Document document, IEnumerable<PrintCadExportItem> items)
    {
        ViewSet viewSet = new();
        foreach (PrintCadExportItem item in items)
        {
            if (document.GetElement(RevitElementIds.Create(item.ElementId)) is View view)
            {
                viewSet.Insert(view);
            }
        }

        return viewSet;
    }

    private static void AddFailureForItems(
        PrintCadExportFormat format,
        IReadOnlyList<PrintCadExportItem> items,
        List<PrintCadExportFailure> failures,
        string message)
    {
        failures.AddRange(items.Select(item => new PrintCadExportFailure(format, item, message)));
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
