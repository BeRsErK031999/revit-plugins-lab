using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace ModelSheetPublisher
{
    public sealed class Media
    {
        public string Name;
        public double Width, Height;
    }
    public static class Publisher
    {
        public static List<Media> GetMedia(string device)
        {
            using (var ps = new PlotSettings(true))
            {
                var v = PlotSettingsValidator.Current;
                v.SetPlotConfigurationName(ps, device, null); v.RefreshLists(ps);
                var media = new List<Media>();
                foreach (string name in v.GetCanonicalMediaNameList(ps))
                {
                    v.SetCanonicalMediaName(ps, name);
                    v.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
                    var margins = ps.PlotPaperMargins;
                    if (Math.Abs(margins.MinPoint.X) > .01 || Math.Abs(margins.MinPoint.Y) > .01 || Math.Abs(margins.MaxPoint.X) > .01 || Math.Abs(margins.MaxPoint.Y) > .01) continue;
                    media.Add(new Media { Name = name, Width = ps.PlotPaperSize.X, Height = ps.PlotPaperSize.Y });
                }
                if (media.Count == 0) throw new InvalidOperationException("У PDF-плоттера нет бумаги без полей. Выберите DWG To PDF.pc3 или AutoCAD PDF с full bleed.");
                return media;
            }
        }

        public static Document FindDocument(string source)
        {
            foreach (Document doc in AcApp.DocumentManager)
                if (string.Equals(doc.Name, source, StringComparison.OrdinalIgnoreCase)) return doc;
            if (!File.Exists(source)) throw new FileNotFoundException("Исходный DWG не открыт и не найден: " + source);
            return HostDocuments.Open(source);
        }

        public static void Publish(IList<Sheet> sheets, PublishOptions options, Func<int, string, bool> progress)
        {
            Trace("Publish entry");
            Bootstrap.EnsureInitialized();
            using var dependencyScope = Bootstrap.PdfScope();
            if (sheets.Count == 0) throw new InvalidOperationException("Добавьте хотя бы один лист.");
            Trace("Plot state");
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting) throw new InvalidOperationException("Дождитесь завершения текущей печати AutoCAD.");
            Trace("Output names");
            var targets = OutputNames.Get(options, sheets.Count);
            foreach (var s in sheets) s.Validate();
            if (!options.Overwrite && targets.Any(File.Exists)) throw new IOException("Файлы с таким именем уже существуют. Подтвердите перезапись в окне сохранения PDF.");
            Directory.CreateDirectory(options.Folder);
            string stage = Path.Combine(options.Folder, ".msp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            Trace("Stage " + stage);
            var initial = AcApp.DocumentManager.MdiActiveDocument;
            object background = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            var opened = new List<Document>();
            var existing = new List<Document>();
            foreach (Document d in AcApp.DocumentManager) existing.Add(d);
            Trace("Existing docs " + existing.Count);
            bool retainRecovery = false;
            var log = new List<string> { "Model Sheet Publisher " + typeof(Publisher).Assembly.GetName().Version,
                DateTime.Now.ToString("O"), "AutoCAD: " + AcApp.GetSystemVariable("ACADVER"), "Runtime: " + Environment.Version,
                "Device: " + options.Device, "Style: " + options.Style };
            try
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", 0);
                var media = GetMedia(options.Device);
                Trace("Media " + media.Count);
                var docs = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
                foreach (var source in sheets.Select(s => s.Source).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var doc = FindDocument(source); docs[source] = doc;
                    if (!existing.Contains(doc)) opened.Add(doc);
                    if (options.UseStyles && !string.IsNullOrEmpty(options.Style) && doc.Database.PlotStyleMode != options.Style.EndsWith(".ctb", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Тип CTB/STB не соответствует чертежу: " + source);
                }
                var pages = new List<string>();
                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];
                    if (!progress(i, $"Лист {i + 1}/{sheets.Count}: {sheet.Drawing}")) throw new OperationCanceledException();
                    var doc = docs[sheet.Source];
                    AcApp.DocumentManager.MdiActiveDocument = doc;
                    using (doc.LockDocument())
                    {
                        var suitable = media.Where(m => m.Width >= sheet.PaperWidth - .01 && m.Height >= sheet.PaperHeight - .01).OrderBy(m => m.Width * m.Height).FirstOrDefault();
                        var selected = suitable ?? media.OrderByDescending(m => m.Width * m.Height).First();
                        var tiles = TilePlanner.Plan(sheet, selected.Width, selected.Height);
                        var tileFiles = new List<string>();
                        try
                        {
                            for (int t = 0; t < tiles.Count; t++)
                            {
                                if (!progress(i, $"Лист {i + 1}/{sheets.Count}, участок {t + 1}/{tiles.Count}")) throw new OperationCanceledException();
                                string raw = Path.Combine(stage, $"raw-{i:D3}-{t:D2}.pdf");
                                PlotTile(doc, sheet, tiles[t], options, selected, raw); tileFiles.Add(raw);
                            }
                            string pagePath = Path.Combine(stage, $"page-{i:D3}.pdf");
                            PdfComposer.ComposePage(sheet, selected.Width, selected.Height, tiles, tileFiles, pagePath);
                            pages.Add(pagePath);
                            log.Add($"{i + 1}: {sheet.Source} | {sheet.Window} | {sheet.Size} mm | {sheet.Scale} | tiles={tiles.Count} | {sheet.Warning}");
                        }
                        finally { foreach (var file in tileFiles) if (File.Exists(file)) File.Delete(file); }
                    }
                }
                if (!progress(sheets.Count, "Сборка PDF…")) throw new OperationCanceledException();
                var stagedOutputs = new List<string>();
                if (options.Separate) stagedOutputs.AddRange(pages);
                else
                {
                    var mergedPath = Path.Combine(stage, "combined.pdf");
                    PdfComposer.CombinePages(pages, mergedPath, options.BaseName);
                    stagedOutputs.Add(mergedPath);
                }
                string stagedLog = Path.Combine(stage, "publish.log"); File.WriteAllLines(stagedLog, log);
                stagedOutputs.Add(stagedLog);
                var allTargets = targets.ToList();
                allTargets.Add(Path.Combine(options.Folder, options.BaseName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log"));
                OutputCommitter.Commit(stagedOutputs, allTargets, options.Overwrite, stage);
            }
            catch (OutputRecoveryException)
            {
                retainRecovery = true;
                throw;
            }
            finally
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", background);
                foreach (var doc in opened) HostDocuments.Close(doc);
                if (initial != null && AcApp.DocumentManager.Cast<Document>().Contains(initial)) AcApp.DocumentManager.MdiActiveDocument = initial;
                // The stage is an exact, newly-created directory; source drawings are never written.
                if (!retainRecovery && Directory.Exists(stage)) Directory.Delete(stage, true);
            }
        }

        private static void Trace(string message)
        {
            if (Environment.GetEnvironmentVariable("MSP_DIAGNOSTICS") == "1") File.AppendAllText(Path.Combine(Path.GetTempPath(), "msp-trace.txt"), DateTime.Now.ToString("O") + " " + message + "\n");
        }

        private static void PlotTile(Document doc, Sheet sheet, Tile tile, PublishOptions options, Media media, string output)
        {
            var editor = doc.Editor;
            using (var originalView = editor.GetCurrentView())
            {
                var lm = LayoutManager.Current;
                string previousLayout = lm.CurrentLayout;
                try
                {
                    if (previousLayout != "Model") lm.CurrentLayout = "Model";
                    using (var view = editor.GetCurrentView())
                    {
                        view.ViewDirection = Vector3d.ZAxis; view.Target = Point3d.Origin; view.ViewTwist = 0; view.PerspectiveEnabled = false;
                        editor.SetCurrentView(view);
                    }
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var layout = (Layout)tr.GetObject(lm.GetLayoutId("Model"), OpenMode.ForRead);
                        using (var ps = new PlotSettings(true))
                        using (var info = new PlotInfo())
                        using (var validator = new PlotInfoValidator())
                        {
                            ps.CopyFrom(layout);
                            var v = PlotSettingsValidator.Current;
                            v.SetPlotConfigurationName(ps, options.Device, null); v.RefreshLists(ps);
                            v.SetCanonicalMediaName(ps, media.Name);
                            v.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
                            v.SetPlotRotation(ps, PlotRotation.Degrees000);
                            v.SetPlotWindowArea(ps, new Extents2d(tile.Window.X, tile.Window.Y, tile.Window.Right, tile.Window.Top));
                            v.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                            v.SetUseStandardScale(ps, false);
                            v.SetCustomPrintScale(ps, new CustomScale(sheet.PrintScale, 1));
                            v.SetPlotCentered(ps, false); v.SetPlotOrigin(ps, Point2d.Origin);
                            ps.PrintLineweights = options.Lineweights; ps.PlotPlotStyles = options.UseStyles;
                            ps.ScaleLineweights = false; ps.PlotTransparency = options.Transparency;
                            if (options.UseStyles) v.SetCurrentStyleSheet(ps, options.Style);
                            info.Layout = layout.ObjectId; info.OverrideSettings = ps;
                            validator.MediaMatchingPolicy = MatchingPolicy.MatchEnabled; validator.Validate(info);
                            if (Math.Abs(info.ValidatedSettings.PlotPaperSize.X - media.Width) > .01 || Math.Abs(info.ValidatedSettings.PlotPaperSize.Y - media.Height) > .01)
                                throw new InvalidOperationException("Плоттер заменил выбранный размер бумаги.");
                            using (var engine = PlotFactory.CreatePublishEngine())
                            {
                                engine.BeginPlot(null, null);
                                engine.BeginDocument(info, doc.Name, null, 1, true, output);
                                using (var pageInfo = new PlotPageInfo())
                                {
                                    engine.BeginPage(pageInfo, info, true, null); engine.BeginGenerateGraphics(null);
                                    engine.EndGenerateGraphics(null); engine.EndPage(null);
                                }
                                engine.EndDocument(null); engine.EndPlot(null);
                            }
                        }
                        tr.Commit();
                    }
                }
                finally
                {
                    lm.CurrentLayout = previousLayout;
                    editor.SetCurrentView(originalView);
                }
            }
            if (!File.Exists(output) || new FileInfo(output).Length < 100) throw new IOException("AutoCAD не создал PDF: " + output);
        }

    }
}
