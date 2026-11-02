using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Windows;
using ModelSheetPublisher;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(RevisionCheck))]
public class RevisionCheck
{
    [CommandMethod("MSPREVISIONCHECK", CommandFlags.Session)]
    public void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "msp-revision-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        try
        {
            var tabs = ComponentManager.Ribbon.Tabs.Where(t => t.Id == "ModelSheetPublisher.Tab").ToList();
            if (tabs.Count != 1 || tabs[0].Panels[0].Source.Items.Count != 1) throw new InvalidOperationException("Ribbon missing/duplicated");
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var frames = FrameFinder.Read(doc.Database);
            int poly = 0, lines = 0, blocks = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    var found = FrameFinder.FromEntity(doc.Database, id, new Point3d(100, 1100, 0), frames);
                    if (entity is Polyline && found != null) poly++;
                    if (entity is Line line)
                    {
                        bool diagonal = Math.Abs(line.StartPoint.X - line.EndPoint.X) > .1 && Math.Abs(line.StartPoint.Y - line.EndPoint.Y) > .1;
                        if (diagonal && found != null) throw new InvalidOperationException("Interior diagonal selected a frame");
                        if (found != null) lines++;
                    }
                    if (entity is BlockReference && found != null && Math.Abs(found.Width - 1188) < .01) blocks++;
                }
            }
            if (poly != 2 || lines != 4 || blocks != 1) throw new InvalidOperationException($"Frame counts {poly}/{lines}/{blocks}");
            var sheets = new[] { Sheet.Create(doc.Name, new Box(0, 0, 420, 297), 1), Sheet.Create(doc.Name, new Box(1000, 0, 630, 297), 1) };
            var options = new PublishOptions { Folder = folder, BaseName = "replaced", Overwrite = true, UseStyles = false };
            File.WriteAllText(Path.Combine(folder, "replaced.pdf"), "old-combined");
            Publisher.Publish(sheets, options, (i, m) => true);
            if (new FileInfo(Path.Combine(folder, "replaced.pdf")).Length < 100) throw new InvalidOperationException("Combined not replaced");
            options.Separate = true;
            var targets = OutputNames.Get(options, 2); foreach (var target in targets) File.WriteAllText(target, "old");
            Publisher.Publish(sheets, options, (i, m) => true);
            var before = targets.Select(File.ReadAllBytes).ToArray();
            try { Publisher.Publish(sheets, options, (i, m) => i < 1); throw new InvalidOperationException("No cancel"); }
            catch (OperationCanceledException) { }
            if (targets.Where((p, i) => !File.ReadAllBytes(p).SequenceEqual(before[i])).Any()) throw new InvalidOperationException("Cancel changed old PDF");
            using (var locked = new FileStream(targets[1], FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { Publisher.Publish(sheets, options, (i, m) => true); throw new InvalidOperationException("No locked-file failure"); }
                catch (IOException) { }
            }
            if (targets.Where((p, i) => !File.ReadAllBytes(p).SequenceEqual(before[i])).Any()) throw new InvalidOperationException("Rollback changed old PDF");
            if (Directory.GetDirectories(folder, ".msp-*").Any()) throw new InvalidOperationException("Staging leak");
            File.WriteAllText(Path.Combine(folder, "PASS.txt"), typeof(Commands).Assembly.GetName().Version + " ribbon singleton; selected polyline/line/block; interior lines rejected; combined and separate overwrite; cancellation preserves originals; locked second PDF restores first byte-for-byte.\nAssembly: " + typeof(Commands).Assembly.Location);
        }
        catch (System.Exception ex) { File.WriteAllText(Path.Combine(folder, "FAIL.txt"), ex.ToString()); }
    }

    [CommandMethod("MSPENTITYCHECK", CommandFlags.Session)]
    public void Selection()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var field = typeof(Commands).GetField("queue", BindingFlags.Static | BindingFlags.NonPublic);
        object old = field.GetValue(null); var queue = new QueueFile(); field.SetValue(null, queue);
        var original = doc.Editor.CurrentUserCoordinateSystem;
        try
        {
            using (doc.LockDocument()) doc.Editor.CurrentUserCoordinateSystem = Matrix3d.Displacement(new Vector3d(100, 200, 0));
            var select = typeof(Commands).GetMethod("Select", BindingFlags.Static | BindingFlags.NonPublic);
            select.Invoke(null, new object[] { doc.Name, true });
            select.Invoke(null, new object[] { doc.Name, false });
            var expected = new[] { new Box(0, 0, 420, 297), new Box(1000, 0, 630, 297), new Box(0, 1000, 1188, 840), new Box(100, 200, 210, 297) };
            if (queue.Sheets.Count != 4) throw new InvalidOperationException("Count " + queue.Sheets.Count);
            for (int i = 0; i < 4; i++) if (queue.Sheets[i].Window.ToString() != expected[i].ToString()) throw new InvalidOperationException("Window " + i + ": " + queue.Sheets[i].Window);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "msp-entity-011.txt"), "PASS GetEntity: polyline, exploded line, transformed block, then GetCorner rectangle; translated UCS; Enter return.");
        }
        catch (System.Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "msp-entity-011.txt"), ex.ToString()); }
        finally { field.SetValue(null, old); using (doc.LockDocument()) doc.Editor.CurrentUserCoordinateSystem = original; }
    }
}
