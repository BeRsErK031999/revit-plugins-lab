using System.Diagnostics;
using System.IO;
using System.Threading;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintDriverPdfService
{
    public PrintPdfExportResult Print(Document document, string folder, IReadOnlyList<PrintPdfExportItem> items,
        bool combined, string? combinedName, string printer, string? setup, bool automaticPaper,
        PrintPdfExportSettings settings, ITrueBimLogger logger, Action<PrintOperationProgressStep>? progress)
    {
        var files = new List<string>();
        var failures = new List<PrintPdfExportFailure>();
        IReadOnlyDictionary<long, string> largerPaper = new Dictionary<long, string>();
        string staging = Path.Combine(folder, ".truebim-print-" + Guid.NewGuid().ToString("N"));
        string progressName = PrintPdfExportService.BuildCombinedPdfFileName(combinedName);
        if (combined) progress?.Invoke(new("Печать", progressName, PrintOperationProgressPhase.Started));
        try
        {
            Directory.CreateDirectory(staging);
            var paths = items.ToDictionary(i => i.ElementId, i => Path.GetFullPath(Path.Combine(staging, i.ElementId + ".pdf")));
            var jobs = items.Select(i =>
            {
                var sheet = document.GetElement(RevitElementIds.Create(i.ElementId)) as ViewSheet;
                return new PrintDriverJobItem(i.ElementId, sheet?.SheetNumber ?? "", sheet?.Name ?? i.FileName);
            }).ToList();
            var printed = new PrintManagerService().PrintCore(document, jobs, printer, setup, logger,
                combined ? null : progress, settings, automaticPaper, paths);
            largerPaper = printed.LargerPaper;
            var errors = printed.Failures.ToDictionary(f => f.Item.ElementId, f => f.Message);
            var ready = new List<string>();
            var wait = Stopwatch.StartNew();
            foreach (var item in items)
            {
                try
                {
                    if (errors.TryGetValue(item.ElementId, out var message)) throw new IOException(message);
                    string path = paths[item.ElementId];
                    WaitForSpool(path, wait);
                    PreparePdf(path, printer);
                    using (var pdf = PdfDocument.Open(path))
                    {
                        if (pdf.NumberOfPages != 1) throw new InvalidDataException("Драйвер вернул неверное число страниц.");
                        if (printed.ExpectedPaper.TryGetValue(item.ElementId, out var expected))
                        {
                            var page = pdf.GetPage(1);
                            var actual = new PrintPaperSize(page.Width * 25.4 / 72, page.Height * 25.4 / 72);
                            if (Math.Abs(actual.WidthMm - expected.WidthMm) > 1 || Math.Abs(actual.HeightMm - expected.HeightMm) > 1)
                                throw new InvalidDataException($"Драйвер вывел страницу {actual}. Ожидалось {expected}.");
                        }
                    }
                    if (combined) ready.Add(path);
                    else Publish(path, Path.Combine(folder, PrintPdfExportService.NormalizePdfFileName(item.FileName)), files);
                }
                catch (Exception ex)
                {
                    failures.Add(new PrintPdfExportFailure(item, ex.Message));
                    logger.Error($"Не удалось получить PDF листа {item.FileName} через {printer}.", ex);
                }
            }

            if (combined && items.Count > 0)
            {
                if (failures.Count != 0) throw new IOException("Общий PDF не создан из-за ошибки печати одного или нескольких листов.");
                string merged = Path.Combine(staging, "combined.pdf");
                using (var stream = File.Create(merged)) PdfMerger.Merge(stream, ready.ToArray());
                using (var pdf = PdfDocument.Open(merged))
                    if (pdf.NumberOfPages != items.Count) throw new InvalidDataException("В общем PDF неверное число страниц.");
                Publish(merged, Path.Combine(folder, PrintPdfExportService.BuildCombinedPdfFileName(combinedName)), files);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Ошибка пакетной печати в PDF через {printer}.", ex);
            var failed = new HashSet<long>(failures.Select(f => f.Item.ElementId));
            failures.AddRange(items.Where(i => !failed.Contains(i.ElementId)).Select(i => new PrintPdfExportFailure(i, ex.Message)));
        }
        finally
        {
            if (combined) progress?.Invoke(new("Печать", progressName, PrintOperationProgressPhase.Completed));
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.Warning($"Не удалось очистить папку задания печати: {ex.Message}"); }
        }
        return new PrintPdfExportResult(files, failures) { LargerPaper = largerPaper };
    }

    private static void Publish(string source, string destination, ICollection<string> files)
    {
        if (File.Exists(destination)) File.Replace(source, destination, null);
        else File.Move(source, destination);
        files.Add(destination);
    }

    private static void WaitForSpool(string path, Stopwatch wait)
    {
        while (true)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                if (stream.Length > 4) return;
            }
            catch (IOException) { }
            if (wait.Elapsed >= TimeSpan.FromMinutes(2))
                throw new IOException("Драйвер не завершил запись файла за две минуты.");
            Thread.Sleep(150);
        }
    }

    private static void PreparePdf(string path, string printer)
    {
        using (var input = File.OpenRead(path))
        {
            var header = new byte[5];
            if (input.Read(header, 0, header.Length) == header.Length &&
                System.Text.Encoding.ASCII.GetString(header) == "%PDF-") return;
        }
        if (printer.IndexOf("PDF24", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidDataException($"Принтер «{printer}» не вернул PDF. Для записи в папку выберите Microsoft Print to PDF или PDF24.");
        string? converter = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), "PDF24", "pdf24-DocTool.exe")).FirstOrDefault(File.Exists);
        if (converter is null) throw new FileNotFoundException("Не найден PDF24 Creator для преобразования потока принтера.");
        string spool = Path.ChangeExtension(path, ".ps");
        File.Move(path, spool);
        using var process = Process.Start(new ProcessStartInfo(converter,
            $"-convertToPDF -noProgress -profile default/good -outputFile \"{path}\" \"{spool}\"")
        { CreateNoWindow = true, UseShellExecute = false });
        if (process is null) throw new IOException("Не удалось запустить PDF24.");
        if (!process.WaitForExit(120000))
        {
            process.Kill();
            throw new IOException("PDF24 не завершил преобразование за две минуты.");
        }
        if (process.ExitCode != 0 || !File.Exists(path)) throw new IOException("PDF24 не создал файл PDF.");
    }
}
