using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintPdfExportService
{
    public PrintPdfExportResult Export(
        Document document,
        string exportFolder,
        IReadOnlyList<PrintPdfExportItem> items,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(exportFolder, nameof(exportFolder));
        Guard.NotNull(items, nameof(items));
        Guard.NotNull(logger, nameof(logger));

        List<string> exportedFiles = new();
        List<PrintPdfExportFailure> failures = new();

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

                using PDFExportOptions options = new()
                {
                    Combine = true,
                    FileName = pdfFileName,
                    StopOnError = true
                };

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
                logger.Info($"Exported PDF: {outputPath}");
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to export PDF for sheet element id {item.ElementId}.", exception);
                failures.Add(new PrintPdfExportFailure(item, exception.Message));
            }
        }

        return new PrintPdfExportResult(exportedFiles, failures);
    }

    public static string NormalizePdfFileName(string fileName)
    {
        Guard.NotNullOrWhiteSpace(fileName, nameof(fileName));

        string normalizedFileName = Path.GetFileName(fileName.Trim());
        return string.Equals(Path.GetExtension(normalizedFileName), ".pdf", StringComparison.OrdinalIgnoreCase)
            ? normalizedFileName
            : normalizedFileName + ".pdf";
    }
}
