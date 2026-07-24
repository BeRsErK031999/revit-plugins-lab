namespace TrueBIM.App.Modules.Lintels.Models;

public sealed record LintelTypeImageResult(
    bool ImageExported,
    bool TypeImageAssigned,
    string? ImageFilePath,
    int PixelWidth,
    int PixelHeight,
    IReadOnlyList<string> Messages)
{
    public bool ModelChanged => TypeImageAssigned;

    public string BuildSummary()
    {
        string size = PixelWidth > 0 && PixelHeight > 0
            ? $" Размер: {PixelWidth} × {PixelHeight} px."
            : string.Empty;
        List<string> lines =
        [
            $"PNG — {(ImageExported ? "экспортирован" : "не экспортирован")}; «Изображение типоразмера» — {(TypeImageAssigned ? "назначено" : "не назначено")}.{size}"
        ];
        if (!string.IsNullOrWhiteSpace(ImageFilePath))
        {
            lines.Add($"Файл: {ImageFilePath}");
        }

        lines.AddRange(Messages.Where(message => !string.IsNullOrWhiteSpace(message)));
        return string.Join(Environment.NewLine, lines);
    }

    public static LintelTypeImageResult Failed(
        string message,
        bool imageExported = false,
        string? imageFilePath = null)
    {
        return new LintelTypeImageResult(
            imageExported,
            false,
            imageFilePath,
            0,
            0,
            [message]);
    }
}
