namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed class OpeningViewAnnotationPreview
{
    public OpeningViewAnnotationPreview(
        string title,
        bool canCreateTitle,
        bool canCreateWidthDimension,
        bool canCreateHeightDimension,
        IReadOnlyList<string>? warnings = null)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Без марки" : title.Trim();
        CanCreateTitle = canCreateTitle;
        CanCreateWidthDimension = canCreateWidthDimension;
        CanCreateHeightDimension = canCreateHeightDimension;
        Warnings = warnings ?? [];
    }

    public string Title { get; }

    public bool CanCreateTitle { get; }

    public bool CanCreateWidthDimension { get; }

    public bool CanCreateHeightDimension { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool CanApply => CanCreateTitle || CanCreateWidthDimension || CanCreateHeightDimension;

    public string ToDialogText()
    {
        List<string> lines =
        [
            $"Марка над видом: {Title} ({FormatReady(CanCreateTitle)})",
            $"Размер ширины проёма: {FormatReady(CanCreateWidthDimension)}",
            $"Размер высоты проёма: {FormatReady(CanCreateHeightDimension)}"
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
}
