using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ModelSheetPublisher;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string folder = args.Length > 0 ? args[0] : Path.GetTempPath(); Directory.CreateDirectory(folder);
        var queue = new QueueFile(); queue.Options.BaseName = "Пример листов в модели";
        for (int i = 0; i < 200; i++) queue.Sheets.Add(Sheet.Create(i % 2 == 0 ? @"C:\Проект\Планы.dwg" : @"C:\Проект\Схемы.dwg", new Box(0, i * 30000, 42000, 29700), 1));
        var catalog = new UiCatalog { ActiveDrawing = @"C:\Проект\Планы.dwg" };
        catalog.Drawings.Add(catalog.ActiveDrawing); catalog.Drawings.Add(@"C:\Проект\Схемы.dwg");
        catalog.Devices.Add("DWG To PDF.pc3"); catalog.Styles.Add("monochrome.ctb");
        using (var form = new MainForm(queue, catalog))
        {
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "main-window.png")); }
            form.Size = form.MinimumSize; Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "main-minimum.png")); }
            form.Close();
        }
        using (var form = new SheetForm(queue.Sheets.Take(2).ToList()))
        {
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "sheet-settings.png")); }
            form.Close();
        }
        using (var form = new OutputForm(queue.Options, 200, true))
        {
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "output-dialog.png")); }
            form.Close();
        }
        Console.WriteLine("UI snapshots generated.");
    }
}
