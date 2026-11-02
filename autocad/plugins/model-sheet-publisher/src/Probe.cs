using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(ModelSheetPublisher.Probe))]

namespace ModelSheetPublisher
{
    public class Probe
    {
        [CommandMethod("MSPFIXTURE", CommandFlags.Modal)]
        public void CreateFixture()
        {
            string path = Path.Combine(Path.GetTempPath(), "msp-fixture.dwg");
            using (var db = new Database(true, true))
            {
                db.Insunits = UnitsValue.Millimeters;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    var ms = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    Action<BlockTableRecord, double, double, double, double, bool> draw = (owner, x, y, w, h, lines) =>
                    {
                        var corners = new[] { new Point2d(x, y), new Point2d(x + w, y), new Point2d(x + w, y + h), new Point2d(x, y + h) };
                        Action<Entity> add = entity => { entity.SetDatabaseDefaults(db); entity.ColorIndex = 7; owner.AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true); };
                        if (lines)
                            for (int i = 0; i < 4; i++) add(new Line(new Point3d(corners[i].X, corners[i].Y, 0), new Point3d(corners[(i + 1) % 4].X, corners[(i + 1) % 4].Y, 0)));
                        else
                        {
                            var p = new Polyline(); for (int i = 0; i < 4; i++) p.AddVertexAt(i, corners[i], 0, 0, 0); p.Closed = true; add(p);
                        }
                        add(new Line(new Point3d(x + 10, y + 10, 0), new Point3d(x + w - 10, y + h - 10, 0)));
                        add(new Line(new Point3d(x + 10, y + h - 10, 0), new Point3d(x + w - 10, y + 10, 0)));
                        add(new DBText { Position = new Point3d(x + 20, y + h - 30, 0), Height = 10, TextString = "TOP LEFT " + w + " x " + h });
                        add(new DBText { Position = new Point3d(x + w - 200, y + 20, 0), Height = 10, TextString = "BOTTOM RIGHT" });
                        add(new Circle(new Point3d(x + w / 2, y + h / 2, 0), Vector3d.ZAxis, 30));
                    };
                    draw(ms, 0, 0, 420, 297, false);
                    draw(ms, 1000, 0, 630, 297, true);
                    var block = new BlockTableRecord { Name = "MSP_FRAME" }; table.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                    draw(block, 0, 0, 594, 420, false);
                    var reference = new BlockReference(new Point3d(0, 1000, 0), block.ObjectId) { ScaleFactors = new Scale3d(2) };
                    ms.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                    draw(ms, 3000, 1000, 2523, 1189, false);
                    tr.Commit();
                }
                db.SaveAs(path, DwgVersion.AC1032);
            }
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nFIXTURE " + path + "\n");
        }

        [CommandMethod("MSPSUITE", CommandFlags.Session)]
        public void Suite()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var folder = Path.Combine(Path.GetTempPath(), "msp-suite-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
            try
            {
                var frames = FrameFinder.Read(doc.Database);
                if (!frames.Any(b => Math.Abs(b.Width - 630) < .01 && Math.Abs(b.X - 1000) < .01)) throw new InvalidOperationException("Exploded frame not recognized");
                if (!frames.Any(b => Math.Abs(b.Width - 1188) < .01 && Math.Abs(b.Y - 1000) < .01)) throw new InvalidOperationException("Transformed block frame not recognized");
                var papers = Paper.Catalog();
                var sheets = new List<Sheet>();
                for (int i = 0; i < 200; i++)
                {
                    var p = papers[i % papers.Count];
                    var s = Sheet.Create(doc.Name, new Box(0, 0, 420, 297), 1);
                    s.Format = p.Name; s.PaperWidth = p.Width; s.PaperHeight = p.Height; s.Margin = 5;
                    sheets.Add(s);
                }
                sheets[0] = Sheet.Create(doc.Name, new Box(3000, 1000, 2523, 1189), 1);
                sheets[0].PaperWidth = 2523; sheets[0].PaperHeight = 1189; sheets[0].Fit = false; sheets[0].PaperUnits = sheets[0].DrawingUnits = 1;
                var options = new PublishOptions { Folder = folder, BaseName = "all-200", UseStyles = false };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Publisher.Publish(sheets, options, (i, msg) => { File.WriteAllText(Path.Combine(folder, "progress.txt"), msg); return true; });
                options.Separate = true; options.BaseName = "separate";
                Publisher.Publish(sheets.Take(2).ToList(), options, (i, msg) => true);
                options.BaseName = "cancelled";
                try { Publisher.Publish(sheets.Take(2).ToList(), options, (i, msg) => i < 1); throw new InvalidOperationException("Cancel did not abort"); }
                catch (OperationCanceledException) { }
                if (Directory.GetFiles(folder, "cancelled*.pdf").Any()) throw new InvalidOperationException("Cancel left final output");
                options.BaseName = "separate";
                try { Publisher.Publish(sheets.Take(2).ToList(), options, (i, msg) => true); throw new InvalidOperationException("Existing files were overwritten"); }
                catch (IOException) { }
                File.WriteAllText(Path.Combine(folder, "PASS.txt"), "200-page mixed-format PDF; separate outputs; cancellation rollback; collision rejection; polyline/line/block recognition.\n" + watch.Elapsed);
                AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nSUITE PASS " + folder + "\n");
            }
            catch (System.Exception ex) { File.WriteAllText(Path.Combine(folder, "FAIL.txt"), ex.ToString()); doc.Editor.WriteMessage("\nSUITE FAIL " + ex + "\n"); }
        }
        [CommandMethod("MSPSMOKE", CommandFlags.Session)]
        public void Smoke()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            string folder = Path.Combine(Path.GetTempPath(), "msp-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var frames = FrameFinder.Read(doc.Database);
                File.WriteAllLines(Path.Combine(folder, "frames.txt"), frames.OrderByDescending(b => b.Width * b.Height).Select(b => b + " => " + Paper.Guess(b, 1).Name));
                doc.Editor.WriteMessage("\nFRAMES " + frames.Count + " in " + watch.ElapsedMilliseconds + "ms\n");
                var chosen = frames.Where(b => Paper.Guess(b, 1).Name != "Нестандартный").OrderByDescending(b => b.Width * b.Height).Take(3).ToList();
                if (chosen.Count == 0) throw new InvalidOperationException("No frames detected.");
                var sheets = chosen.Select(b => Sheet.Create(doc.Name, b, 1)).ToList();
                File.WriteAllText(Path.Combine(folder, "sheets.txt"), string.Join("\n", sheets.Select(s => s.Size + " " + s.Scale)));
                var options = new PublishOptions { Folder = folder, BaseName = "sample", Style = "monochrome.ctb" };
                File.WriteAllText(Path.Combine(folder, "before-publish.txt"), "ready");
                Publisher.Publish(sheets, options, (i, message) => { doc.Editor.WriteMessage("\n" + message); return true; });
                File.WriteAllText(Path.Combine(folder, "PASS.txt"), "Published " + sheets.Count + " pages\n" + watch.Elapsed);
                doc.Editor.WriteMessage("\nMSPSMOKE PASS " + folder + "\n");
            }
            catch (System.Exception ex)
            {
                File.WriteAllText(Path.Combine(folder, "FAIL.txt"), ex.ToString());
                doc.Editor.WriteMessage("\nMSPSMOKE FAIL " + ex + "\n");
            }
        }

        [CommandMethod("MSPDIAG", CommandFlags.Modal)]
        public void Diagnose()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var sb = new StringBuilder();
            sb.AppendLine("FILE " + doc.Name);
            sb.AppendLine("UNITS " + doc.Database.Insunits);
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var entities = ms.Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
                foreach (var g in entities.GroupBy(e => e.GetType().Name)) sb.AppendLine(g.Key + " " + g.Count());
                foreach (var e in entities.Where(e => e is BlockReference || e is Polyline).Take(200))
                {
                    try { sb.AppendLine(e.Handle + " " + e.GetType().Name + " layer=" + e.Layer + (e is BlockReference b ? " block=" + b.Name : "") + (e is Polyline p ? " closed=" + p.Closed + " vertices=" + p.NumberOfVertices : "") + " " + e.GeometricExtents); } catch { }
                }
                tr.Commit();
            }
            using (var ps = new PlotSettings(true))
            {
                var v = PlotSettingsValidator.Current;
                v.SetPlotConfigurationName(ps, "DWG To PDF.pc3", null);
                v.RefreshLists(ps);
                foreach (string media in v.GetCanonicalMediaNameList(ps))
                {
                    v.SetCanonicalMediaName(ps, media);
                    sb.AppendLine("MEDIA " + media + " " + ps.PlotPaperSize);
                }
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "msp-diagnostic.txt"), sb.ToString());
            doc.Editor.WriteMessage("\nMSPDIAG OK\n");
        }
    }
}
