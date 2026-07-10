namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed class OpeningViewAnnotationPreview
{
    public OpeningViewAnnotationPreview(
        string title,
        bool canCreateTitle,
        bool canCreateWidthDimension,
        bool canCreateHeightDimension,
        IReadOnlyList<string>? warnings = null,
        bool usesCurtainWallGeometry = false,
        bool widthUsesFamilyGeometry = false,
        bool heightUsesFamilyGeometry = false,
        string? widthReferenceSource = null,
        string? heightReferenceSource = null)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Без марки" : title.Trim();
        CanCreateTitle = canCreateTitle;
        CanCreateWidthDimension = canCreateWidthDimension;
        CanCreateHeightDimension = canCreateHeightDimension;
        Warnings = warnings ?? [];
        UsesCurtainWallGeometry = usesCurtainWallGeometry;
        WidthUsesFamilyGeometry = widthUsesFamilyGeometry;
        HeightUsesFamilyGeometry = heightUsesFamilyGeometry;
        WidthReferenceSource = NormalizeSource(widthReferenceSource);
        HeightReferenceSource = NormalizeSource(heightReferenceSource);
    }

    public string Title { get; }

    public bool CanCreateTitle { get; }

    public bool CanCreateWidthDimension { get; }

    public bool CanCreateHeightDimension { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool UsesCurtainWallGeometry { get; }

    public bool WidthUsesFamilyGeometry { get; }

    public bool HeightUsesFamilyGeometry { get; }

    public string WidthReferenceSource { get; }

    public string HeightReferenceSource { get; }

    public bool CanApply => CanCreateTitle || CanCreateWidthDimension || CanCreateHeightDimension;

    public string ToDialogText()
    {
        string widthLabel = UsesCurtainWallGeometry
            ? "Габаритная ширина витража"
            : WidthUsesFamilyGeometry
                ? "Габаритная ширина изделия"
                : "Ширина проёма";
        string heightLabel = UsesCurtainWallGeometry
            ? "Габаритная высота витража"
            : HeightUsesFamilyGeometry
                ? "Габаритная высота изделия"
                : "Высота проёма";
        List<string> lines =
        [
            $"Марка над видом: {Title} ({FormatReady(CanCreateTitle)})",
            $"{widthLabel}: {FormatReady(CanCreateWidthDimension)}{FormatSource(CanCreateWidthDimension, WidthReferenceSource)}",
            $"{heightLabel}: {FormatReady(CanCreateHeightDimension)}{FormatSource(CanCreateHeightDimension, HeightReferenceSource)}"
        ];

        if (Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(Warnings.Select(warning => $"- {warning}"));
        }

        lines.Add(string.Empty);
        lines.Add("Повторный запуск заменит только аннотации, созданные TrueBIM, и не затронет ручное оформление.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatReady(bool value)
    {
        return value ? "готов" : "недоступен";
    }

    private static string NormalizeSource(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static string FormatSource(bool available, string source)
    {
        return available && !string.IsNullOrWhiteSpace(source) ? $" — {source}" : string.Empty;
    }
}
