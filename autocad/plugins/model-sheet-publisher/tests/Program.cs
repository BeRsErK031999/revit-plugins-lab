using ModelSheetPublisher;

int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < .00001, message + $": {actual} != {expected}");
void Reject(Action action, string message) { bool thrown = false; try { action(); } catch { thrown = true; } Check(thrown, message); }

var a3 = Sheet.Create("a.dwg", new Box(100, 200, 42000, 29700), 1);
Check(a3.Format == "A3", "Scaled A3"); Near(a3.PaperWidth, 420, "Landscape"); Near(a3.PrintScale, .01, "Scale 1:100");
var a2 = Sheet.Create("a.dwg", new Box(0, 0, 59400, 42000), 1);
Check(a2.Format == "A2", "Scaled A2 defaults to 1:100 on ambiguous ISO ratio");
Check(Sheet.Create("m.dwg", new Box(0, 0, .42, .297), 1000).Format == "A3", "Meters conversion");
Check(Sheet.Create("a.dwg", new Box(0, 0, 210, 297), 1).Format == "A4", "True A4");
Check(Sheet.Create("a.dwg", new Box(0, 0, 63000, 29700), 1).Format == "A4×3", "Elongated scaled A4");

foreach (var paper in Paper.Catalog())
{
    var sheet = Sheet.Create("formats.dwg", new Box(0, 0, paper.Width, paper.Height), 1);
    Near(sheet.PaperWidth, paper.Width, paper.Name + " width"); Near(sheet.PaperHeight, paper.Height, paper.Name + " height");
    sheet.PaperWidth = paper.Width; sheet.PaperHeight = paper.Height;
    var tiles = TilePlanner.Plan(sheet, 1682, 2378);
    Near(tiles.Sum(t => t.Width * t.Height), paper.Width * paper.Height, paper.Name + " exact area coverage");
    Check(tiles.All(t => t.Width <= 1682 && t.Height <= 2378), paper.Name + " each tile fits driver");
}
var huge = Sheet.Create("long.dwg", new Box(10000, 20000, 252300, 118900), 1);
huge.PaperWidth = 2523; huge.PaperHeight = 1189;
var split = TilePlanner.Plan(huge, 1682, 2378);
Check(split.Count == 2, "A0×3 split"); Near(split[1].Window.X, 178200, "Tile world start"); Near(split[1].X, 1682, "Tile paper start");

a3.Margin = 10;
Near(a3.PrintScale, 277.0 / 29700, "Fit respects margins");
var centered = TilePlanner.Plan(a3, 841, 1189).Single();
Near(centered.Y, 10, "Center Y"); Check(centered.X > 10, "Center X includes spare width");
a3.Fit = false; a3.PaperUnits = 1; a3.DrawingUnits = 50;
Check(a3.Warning.StartsWith("Обрезка"), "Overscale warning");
Near(TilePlanner.Plan(a3, 841, 1189).Sum(t => t.Width * t.Height), 400 * 277, "Clipping does not change scale");
a3.Center = false; a3.OffsetX = 10000;
Reject(() => TilePlanner.Plan(a3, 841, 1189), "Reject fully off-page area");

var hs = new List<Segment> { new(20, 10, 60), new(20, 60, 110), new(220, 10, 110), new(50, 10, 50) };
var vs = new List<Segment> { new(10, 20, 220), new(110, 20, 220) };
var boxes = RectangleGeometry.FromLines(hs, vs).ToList();
Check(boxes.Any(b => b.X == 10 && b.Y == 20 && b.Width == 100 && b.Height == 200), "Exploded segmented rectangle");
Check(!RectangleGeometry.FromLines(hs, vs.Take(1).ToList()).Any(), "Incomplete frame rejected");

var options = new PublishOptions { Folder = Path.GetTempPath(), BaseName = "Тест", Separate = true };
var names = OutputNames.Get(options, 200);
Check(names.Length == 200 && names.Distinct().Count() == 200, "200 unique output names");
Check(names[0].EndsWith("Тест_001.pdf") && names[199].EndsWith("Тест_200.pdf"), "Order in filenames");
options.BaseName = "../wrong"; Reject(() => OutputNames.Get(options, 1), "Reject path traversal");
options.BaseName = "CON"; Reject(() => OutputNames.Get(options, 1), "Reject reserved Windows filename");
options.BaseName = "Чертёж";
var queue = new QueueFile { Options = options, Sheets = Enumerable.Range(1, 200).Select(i => Sheet.Create($"file-{i}.dwg", new Box(i, 0, 420, 297), 1)).Reverse().ToList() };
string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mspqueue");
try
{
    QueueStore.Save(temp, queue); var read = QueueStore.Load(temp);
    Check(read.Sheets.Count == 200 && read.Sheets[0].Source == "file-200.dwg" && read.Sheets[199].Source == "file-1.dwg", "Queue preserves 200-sheet manual order");
    Near(read.Sheets[50].Window.X, 150, "Queue preserves coordinates");
    Check(read.Options.BaseName == "Чертёж", "Queue preserves Unicode");
    File.WriteAllText(temp, "<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><QueueFile>&x;</QueueFile>");
    Reject(() => QueueStore.Load(temp), "XML external entities rejected");
}
finally { File.Delete(temp); }
Check(OutputNames.Normalize(" Проект.PDF ") == "Проект", "Extension is not doubled");
options.Overwrite = true;
try { QueueStore.Save(temp, queue); Check(!QueueStore.Load(temp).Options.Overwrite, "Overwrite approval never persisted"); }
finally { File.Delete(temp); }
string commitRoot = Path.Combine(Path.GetTempPath(), "msp-commit-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(commitRoot);
try
{
    foreach (var scenario in new[] { "new", "replace", "refuse", "locked", "rollback-replace", "rollback-new" })
    {
        string dir = Path.Combine(commitRoot, scenario); Directory.CreateDirectory(dir);
        string stage = Path.Combine(dir, "stage"); Directory.CreateDirectory(stage);
        var inputs = new[] { Path.Combine(stage, "a.pdf"), Path.Combine(stage, "b.pdf") };
        var outputs = new[] { Path.Combine(dir, "a.pdf"), Path.Combine(dir, "b.pdf") };
        File.WriteAllText(inputs[0], "new-a"); File.WriteAllText(inputs[1], "new-b");
        bool hadFirst = scenario != "new" && scenario != "rollback-new";
        if (hadFirst) File.WriteAllText(outputs[0], "old-a");
        FileStream locked = null;
        if (scenario == "locked") locked = new FileStream(outputs[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        if (scenario.StartsWith("rollback")) { File.WriteAllText(outputs[1], "old-b"); locked = new FileStream(outputs[1], FileMode.Open, FileAccess.Read, FileShare.Read); }
        try
        {
            if (scenario == "new" || scenario == "replace")
            {
                OutputCommitter.Commit(inputs, outputs, true, stage);
                Check(File.ReadAllText(outputs[0]) == "new-a" && File.ReadAllText(outputs[1]) == "new-b", scenario + " commit");
                if (hadFirst) Check(File.ReadAllText(Path.Combine(stage, "backup-0000.pdf")) == "old-a", "Original retained until cleanup");
            }
            else
            {
                Reject(() => OutputCommitter.Commit(inputs, outputs, scenario != "refuse", stage), scenario + " rejects");
                Check(hadFirst ? File.ReadAllText(outputs[0]) == "old-a" : !File.Exists(outputs[0]), scenario + " preserves original / removes new");
                if (scenario.StartsWith("rollback")) Check(File.ReadAllText(outputs[1]) == "old-b", "Locked later PDF preserved");
            }
        }
        finally { locked?.Dispose(); }
    }
}
finally { Directory.Delete(commitRoot, true); }
Console.WriteLine($"PASS: {passed} assertions");
