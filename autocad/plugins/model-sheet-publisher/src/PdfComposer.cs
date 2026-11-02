using System;
using System.Collections.Generic;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ModelSheetPublisher
{
    public static class PdfComposer
    {
        public static void CombinePages(IEnumerable<string> pages, string output, string title)
        {
            using (var merged = new PdfDocument())
            {
                merged.Info.Title = title;
                foreach (var path in pages)
                using (var input = PdfReader.Open(path, PdfDocumentOpenMode.Import))
                    foreach (PdfPage page in input.Pages) merged.AddPage(page);
                merged.Save(output);
            }
        }

        public static void ComposePage(Sheet sheet, double mediaWidth, double mediaHeight, IList<Tile> tiles, IList<string> files, string output)
        {
            const double points = 72.0 / 25.4;
            using (var pdf = new PdfDocument())
            {
                var page = pdf.AddPage(); page.Width = XUnit.FromMillimeter(sheet.PaperWidth); page.Height = XUnit.FromMillimeter(sheet.PaperHeight);
                pdf.Info.Creator = "Model Sheet Publisher for AutoCAD 2021–2026";
                using (var graphics = XGraphics.FromPdfPage(page))
                {
                    for (int i = 0; i < tiles.Count; i++)
                    using (var form = XPdfForm.FromFile(files[i]))
                    {
                        if (Math.Abs(form.PointWidth / points - mediaWidth) > 1 || Math.Abs(form.PointHeight / points - mediaHeight) > 1)
                            throw new InvalidOperationException("PDF-плоттер изменил размер или ориентацию бумаги. Выберите DWG To PDF.pc3.");
                        var tile = tiles[i]; var state = graphics.Save();
                        graphics.IntersectClip(new XRect(tile.X * points, (sheet.PaperHeight - tile.Y - tile.Height) * points, tile.Width * points, tile.Height * points));
                        graphics.DrawImage(form, tile.X * points, (sheet.PaperHeight - tile.Y - mediaHeight) * points, mediaWidth * points, mediaHeight * points);
                        graphics.Restore(state);
                    }
                }
                pdf.Save(output);
            }
        }
    }
}
