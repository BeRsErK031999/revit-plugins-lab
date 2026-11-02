using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using ModelSheetPublisher;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(SelectionCheck))]
public class SelectionCheck
{
    [CommandMethod("MSPPROGRESSCHECK", CommandFlags.Session)]
    public void ProgressCheck()
    {
        string folder = Path.Combine(Path.GetTempPath(), "msp-progress-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        try
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var a = Sheet.Create(doc.Name, new Box(1664855.276, 783875.931, 59400, 42000), 1);
            var b = Sheet.Create(Path.Combine(Path.GetTempPath(), "msp-fixture.dwg"), new Box(3000, 1000, 2523, 1189), 1);
            typeof(Commands).GetMethod("RunPublish", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { new[] { a, b }, new PublishOptions { Folder = folder, BaseName = "modal-two-drawings", Style = "monochrome.ctb" }, false });
            if (!File.Exists(Path.Combine(folder, "modal-two-drawings.pdf"))) throw new InvalidOperationException("No output");
            File.WriteAllText(Path.Combine(folder, "PASS.txt"), "Progress window successfully published two DWGs.");
        }
        catch (System.Exception ex) { File.WriteAllText(Path.Combine(folder, "FAIL.txt"), ex.ToString()); }
    }

    [CommandMethod("MSPSELECTCHECK", CommandFlags.Session)]
    public void Run()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        string result = Path.Combine(Path.GetTempPath(), "msp-selection-check.txt");
        var queueField = typeof(Commands).GetField("queue", BindingFlags.Static | BindingFlags.NonPublic);
        var select = typeof(Commands).GetMethod("Select", BindingFlags.Static | BindingFlags.NonPublic);
        object oldQueue = queueField.GetValue(null);
        var queue = new QueueFile(); queueField.SetValue(null, queue);
        var ucs = doc.Editor.CurrentUserCoordinateSystem;
        object snaps = AcApp.GetSystemVariable("OSMODE");
        try
        {
            using (doc.LockDocument()) doc.Editor.CurrentUserCoordinateSystem = Matrix3d.Displacement(new Vector3d(100, 200, 0));
            AcApp.SetSystemVariable("OSMODE", 0);
            select.Invoke(null, new object[] { doc.Name, true });
            select.Invoke(null, new object[] { doc.Name, false });
            if (queue.Sheets.Count != 2 || queue.Sheets[0].Format != "A2" || queue.Sheets[1].Format != "A3") throw new InvalidOperationException("Selection count / formats wrong: " + queue.Sheets.Count);
            var box = queue.Sheets[1].Window;
            if (Math.Abs(box.X - 1664855) > .01 || Math.Abs(box.Y - 842086.957) > .01 || Math.Abs(box.Width - 42000) > .01 || Math.Abs(box.Height - 29700) > .01)
                throw new InvalidOperationException("UCS point conversion wrong: " + box);
            QueueStore.Save(Path.Combine(Path.GetTempPath(), "msp-selected.mspqueue"), queue);
            File.WriteAllText(result, "PASS: managed GetEntity frame selection, GetCorner window selection, Enter return, translated UCS conversion; A2 + A3.");
        }
        catch (System.Exception ex) { File.WriteAllText(result, ex.ToString()); }
        finally
        {
            queueField.SetValue(null, oldQueue);
            using (doc.LockDocument()) doc.Editor.CurrentUserCoordinateSystem = ucs;
            AcApp.SetSystemVariable("OSMODE", snaps);
        }
    }
}
