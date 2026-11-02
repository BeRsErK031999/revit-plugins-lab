using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ModelSheetPublisher
{
    public sealed class Box
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Right => X + Width;
        public double Top => Y + Height;
        public Box() { }
        public Box(double x, double y, double width, double height)
        { X = x; Y = y; Width = width; Height = height; }
        public bool Contains(double x, double y, double tolerance = 0)
            => x >= X - tolerance && x <= Right + tolerance && y >= Y - tolerance && y <= Top + tolerance;
        public double EdgeDistance(double x, double y)
        {
            if (!Contains(x, y)) return double.MaxValue;
            return Math.Min(Math.Min(x - X, Right - x), Math.Min(y - Y, Top - y));
        }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},{3:F3}", X, Y, Width, Height);
    }

    public sealed class Paper
    {
        public string Name { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public override string ToString() => Name;
        public static List<Paper> Catalog()
        {
            var result = new List<Paper>();
            Action<string, double, double> add = (name, w, h) => result.Add(new Paper { Name = name, Width = w, Height = h });
            add("A4", 210, 297); add("A3", 297, 420); add("A2", 420, 594); add("A1", 594, 841); add("A0", 841, 1189);
            // ISO elongated formats: short side of the base sheet, multiplied short side of the next smaller sheet.
            for (int n = 3; n <= 9; n++) add("A4×" + n, 297, 210 * n);
            for (int n = 3; n <= 7; n++) add("A3×" + n, 420, 297 * n);
            for (int n = 3; n <= 5; n++) add("A2×" + n, 594, 420 * n);
            for (int n = 3; n <= 4; n++) add("A1×" + n, 841, 594 * n);
            for (int n = 2; n <= 3; n++) add("A0×" + n, 1189, 841 * n);
            return result;
        }
        public static Paper Guess(Box box, double millimetersPerUnit)
        {
            double w = Math.Min(box.Width, box.Height) * millimetersPerUnit;
            double h = Math.Max(box.Width, box.Height) * millimetersPerUnit;
            var list = Catalog();
            var exact = list.OrderBy(p => Math.Abs(p.Width - w) + Math.Abs(p.Height - h)).First();
            if (Math.Abs(exact.Width - w) < 2 && Math.Abs(exact.Height - h) < 2) return exact;
            // A scale is ambiguous from geometry alone. Prefer an ISO aspect ratio, then a common drawing scale.
            double[] scales = { 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };
            var candidate = list.OrderBy(p => Math.Abs(Math.Log((w / h) / (p.Width / p.Height))) * 100
                + scales.Min(s => Math.Abs(Math.Log(w / p.Width / s)))
                + .002 * Math.Abs(Math.Log(w / p.Width / 100))).First();
            if (Math.Abs((w / h) / (candidate.Width / candidate.Height) - 1) < .025) return candidate;
            return new Paper { Name = "Нестандартный", Width = Math.Round(w, 1), Height = Math.Round(h, 1) };
        }
    }

    public sealed class Sheet
    {
        public string Source { get; set; }
        public Box Window { get; set; }
        public string Format { get; set; }
        public double PaperWidth { get; set; }
        public double PaperHeight { get; set; }
        public bool Fit { get; set; } = true;
        public bool Center { get; set; } = true;
        public double PaperUnits { get; set; } = 1;
        public double DrawingUnits { get; set; } = 1;
        public double Margin { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public string Drawing => Path.GetFileName(Source);
        public string Size => $"{PaperWidth:0.#} × {PaperHeight:0.#}";
        public string Scale => Fit ? "Вписать" : $"{PaperUnits:0.###} мм = {DrawingUnits:0.###} ед.";
        public double PrintScale => Fit ? Math.Min((PaperWidth - Margin * 2) / Window.Width, (PaperHeight - Margin * 2) / Window.Height) : PaperUnits / DrawingUnits;
        public string Warning => Window.Width * PrintScale > PaperWidth - Margin * 2 + .01 || Window.Height * PrintScale > PaperHeight - Margin * 2 + .01
            ? "Обрезка: масштаб больше листа" : Format == "Нестандартный" ? "Проверьте размер" : "";
        public void Validate()
        {
            var values = new[] { Window.X, Window.Y, Window.Width, Window.Height, PaperWidth, PaperHeight, PaperUnits, DrawingUnits, Margin, OffsetX, OffsetY };
            if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || Window.Width <= 0 || Window.Height <= 0 || PaperUnits <= 0 || DrawingUnits <= 0
                || PaperWidth < 10 || PaperHeight < 10 || PaperWidth > 5080 || PaperHeight > 5080 || Margin < 0 || Margin * 2 >= Math.Min(PaperWidth, PaperHeight))
                throw new InvalidOperationException("Некорректная область, размер бумаги или масштаб: " + Drawing);
        }
        public static Sheet Create(string source, Box box, double mmPerUnit)
        {
            var paper = Paper.Guess(box, mmPerUnit);
            bool landscape = box.Width > box.Height;
            return new Sheet { Source = source, Window = box, Format = paper.Name,
                PaperWidth = landscape ? paper.Height : paper.Width, PaperHeight = landscape ? paper.Width : paper.Height,
                DrawingUnits = Math.Round(box.Width / (landscape ? paper.Height : paper.Width), 6) };
        }
    }

    public sealed class PublishOptions
    {
        public string Device { get; set; } = "DWG To PDF.pc3";
        public string Style { get; set; } = "";
        public bool UseStyles { get; set; } = true;
        public bool Lineweights { get; set; } = true;
        public bool Transparency { get; set; }
        public bool Separate { get; set; }
        [System.Xml.Serialization.XmlIgnore]
        public bool Overwrite { get; set; }
        public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public string BaseName { get; set; } = "Чертёж";
    }

    public static class OutputNames
    {
        public static string Normalize(string name)
        {
            name = (name ?? "").Trim();
            return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4).Trim() : name;
        }
        public static string[] Get(PublishOptions options, int count)
        {
            var name = options.BaseName.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.Length > 150
                || System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Введите допустимое имя файла без пути и расширения.");
            var folder = Path.GetFullPath(options.Folder);
            return options.Separate ? Enumerable.Range(1, count).Select(i => Path.Combine(folder, name + "_" + i.ToString("D3") + ".pdf")).ToArray()
                : new[] { Path.Combine(folder, name + ".pdf") };
        }
    }
}
