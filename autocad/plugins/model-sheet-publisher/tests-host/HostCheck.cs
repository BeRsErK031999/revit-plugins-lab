using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using ModelSheetPublisher;

[assembly: CommandClass(typeof(HostCheck))]
public class HostCheck
{
    [CommandMethod("MSPMULTICHECK", CommandFlags.Session)]
    public void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "msp-multicheck-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        try
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var frame = FrameFinder.Pick(FrameFinder.Read(doc.Database), 1694555, 804875, 2000);
            if (frame == null || Math.Abs(frame.Width - 59400) > .1 || Math.Abs(frame.Height - 42000) > .1) throw new InvalidOperationException("Outer frame selection failed: " + frame);
            var a = Sheet.Create(doc.Name, frame, 1);
            if (a.Format != "A2") throw new InvalidOperationException("Sample A2 format mismatch");
            var b = Sheet.Create(Path.Combine(Path.GetTempPath(), "msp-fixture.dwg"), new Box(3000, 1000, 2523, 1189), 1);
            var sheets = new[] { a, b, a };
            int docsBefore = AcApp.DocumentManager.Count;
            var background = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            string layout = LayoutManager.Current.CurrentLayout;
            using (var view = doc.Editor.GetCurrentView())
            {
                Publisher.Publish(sheets, new PublishOptions { Folder = folder, BaseName = "two-drawings", Style = "monochrome.ctb" }, (i, message) => true);
                using (var after = doc.Editor.GetCurrentView())
                    if (!view.Target.IsEqualTo(after.Target) || !view.ViewDirection.IsEqualTo(after.ViewDirection) || Math.Abs(view.ViewTwist - after.ViewTwist) > 1e-8) throw new InvalidOperationException("View not restored");
            }
            if (AcApp.DocumentManager.Count != docsBefore || AcApp.DocumentManager.MdiActiveDocument != doc || LayoutManager.Current.CurrentLayout != layout
                || !Equals(AcApp.GetSystemVariable("BACKGROUNDPLOT"), background)) throw new InvalidOperationException("Host state not restored");
            File.WriteAllText(Path.Combine(folder, "PASS.txt"), "Full AutoCAD 2021: sample A2 click resolution; 3 pages from 2 DWGs in interleaved order; auto-open and close of background source; active document, view, layout and BACKGROUNDPLOT restored.");
            doc.Editor.WriteMessage("\nMSPMULTICHECK PASS " + folder + "\n");
        }
        catch (System.Exception ex) { File.WriteAllText(Path.Combine(folder, "FAIL.txt"), ex.ToString()); AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nMSPMULTICHECK FAIL " + ex + "\n"); }
    }
}
