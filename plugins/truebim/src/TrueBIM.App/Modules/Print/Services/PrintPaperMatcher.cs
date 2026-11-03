using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public static class PrintPaperMatcher
{
    // Windows reports paper dimensions in hundredths of an inch. ISO sizes are rounded by drivers.
    private const double ToleranceMm = 0.5;

    public static PrintPaperMatch? Find(PrintPaperSize sheet, IEnumerable<PrinterPaper> papers)
    {
        return papers.Select(paper =>
            {
                bool rotate = sheet.WidthMm != sheet.HeightMm &&
                    (sheet.WidthMm > sheet.HeightMm) != (paper.Size.WidthMm > paper.Size.HeightMm);
                double dx = (rotate ? paper.Size.HeightMm : paper.Size.WidthMm) - sheet.WidthMm;
                double dy = (rotate ? paper.Size.WidthMm : paper.Size.HeightMm) - sheet.HeightMm;
                return new { Paper = paper, Rotate = rotate, Dx = dx, Dy = dy,
                    Exact = Math.Abs(dx) <= ToleranceMm && Math.Abs(dy) <= ToleranceMm };
            })
            .Where(p => p.Dx >= -ToleranceMm && p.Dy >= -ToleranceMm)
            .OrderByDescending(p => p.Exact)
            .ThenBy(p => p.Exact ? Math.Abs(p.Dx) + Math.Abs(p.Dy) : p.Paper.Size.WidthMm * p.Paper.Size.HeightMm)
            .ThenBy(p => Math.Max(p.Dx, p.Dy))
            .ThenBy(p => p.Paper.Name, StringComparer.Ordinal)
            .Select(p => new PrintPaperMatch(p.Paper, p.Rotate, p.Exact))
            .FirstOrDefault();
    }
}
