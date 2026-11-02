using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ModelSheetPublisher;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Application = System.Windows.Forms.Application;

class Program
{
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static IEnumerable<Control> Controls(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Controls(c)));
    [STAThread]
    static void Main(string[] args)
    {
        string folder = Path.GetFullPath(args.Single()); Directory.CreateDirectory(folder);
        const double mediaWidth = 1682, mediaHeight = 2378;
        var sheet = Sheet.Create("test.dwg", new Box(0, 0, 2523, 1189), 1); sheet.PaperWidth = 2523; sheet.PaperHeight = 1189;
        var tiles = TilePlanner.Plan(sheet, mediaWidth, mediaHeight);
        var raw = new List<string>();
        for (int i = 0; i < tiles.Count; i++)
        {
            string file = Path.Combine(folder, "raw-" + i + ".pdf"); raw.Add(file);
            using (var pdf = new PdfDocument())
            {
                var page = pdf.AddPage(); page.Width = XUnit.FromMillimeter(mediaWidth); page.Height = XUnit.FromMillimeter(mediaHeight);
                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    gfx.DrawRectangle(new XPen(XColors.Blue, 2), 10, 10, page.Width.Point - 20, page.Height.Point - 20);
                    gfx.DrawString("Проверка PDF / Tile " + (i + 1), new XFont("Arial", 18), XBrushes.Black, 30, 50);
                }
                pdf.Save(file);
            }
        }
        string longPage = Path.Combine(folder, "Длинный лист.pdf");
        PdfComposer.ComposePage(sheet, mediaWidth, mediaHeight, tiles, raw, longPage);
        using (var read = PdfReader.Open(longPage, PdfDocumentOpenMode.Import))
        {
            Check(read.PageCount == 1, "One composed page");
            Check(Math.Abs(read.Pages[0].Width.Millimeter - 2523) < .01 && Math.Abs(read.Pages[0].Height.Millimeter - 1189) < .01, "Exact elongated paper size");
            Check(read.Pages[0].Elements.GetDictionary("/Resources").Elements.ContainsKey("/XObject"), "Vector forms retained");
        }
        string combined = Path.Combine(folder, "Общий PDF.pdf");
        PdfComposer.CombinePages(new[] { longPage, raw[0], longPage }, combined, "Проверка порядка");
        using (var read = PdfReader.Open(combined, PdfDocumentOpenMode.Import))
        {
            Check(read.PageCount == 3, "Merged page count");
            Check(Math.Abs(read.Pages[1].Width.Millimeter - mediaWidth) < .01 && Math.Abs(read.Pages[2].Width.Millimeter - 2523) < .01, "Mixed format order preserved");
            Check(read.Info.Title == "Проверка порядка", "Unicode title preserved");
        }
        Application.EnableVisualStyles();
        var queue = new QueueFile(); queue.Options.Folder = folder;
        for (int i = 0; i < 200; i++) queue.Sheets.Add(Sheet.Create("test.dwg", new Box(0, 0, 420, 297), 1));
        var catalog = new UiCatalog { ActiveDrawing = "test.dwg" }; catalog.Drawings.Add("test.dwg"); catalog.Devices.Add("DWG To PDF.pc3");
        using (var form = new MainForm(queue, catalog))
        {
            form.Size = form.MinimumSize; form.Show(); Application.DoEvents();
            Check(Controls(form).OfType<DataGridView>().Single().Rows.Count == 200, "200-row form loads in this runtime");
            Check(Controls(form).OfType<Button>().Any(b => b.Text == "Имя PDF…"), "Output dialog action available");
            Snapshot(form, Path.Combine(folder, "main.png")); form.Close();
        }
        var options = new PublishOptions { Folder = folder, BaseName = "Исходное имя" };
        using (var form = new OutputForm(options, 200, false))
        {
            form.Show(); Application.DoEvents();
            Controls(form).OfType<TextBox>().First().Text = "Новое имя.PDF";
            Snapshot(form, Path.Combine(folder, "output.png")); form.AcceptButton.PerformClick();
            Check(form.DialogResult == DialogResult.OK && options.BaseName == "Новое имя", "Name dialog commits normalized Unicode name");
        }
        using (var form = new OutputForm(options, 200, false))
        {
            form.Show(); Application.DoEvents(); Controls(form).OfType<TextBox>().First().Text = "Не сохранять";
            form.CancelButton.PerformClick(); Check(options.BaseName == "Новое имя", "Cancel preserves options");
        }
        File.WriteAllText(Path.Combine(folder, "PASS.txt"), "Runtime " + Environment.Version + ": " + passed + " assertions; PDF vector composition/merge and WinForms queue/name dialog.");
        Console.WriteLine("PASS runtime " + Environment.Version + ": " + passed + " assertions");
    }
    private static void Snapshot(Form form, string path)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path); }
    }
}
