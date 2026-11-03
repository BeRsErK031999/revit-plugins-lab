namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintPaperSize(double WidthMm, double HeightMm)
{
    public override string ToString() => $"{WidthMm:0.#} × {HeightMm:0.#} мм";
}

public sealed record PrinterPaper(string Name, PrintPaperSize Size);

public sealed record PrintPaperMatch(PrinterPaper Paper, bool Rotate, bool IsExact)
{
    public PrintPaperSize OrientedSize => Rotate
        ? new PrintPaperSize(Paper.Size.HeightMm, Paper.Size.WidthMm)
        : Paper.Size;
}
