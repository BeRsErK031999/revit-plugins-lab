namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed class OpeningViewAnnotationResult
{
    public OpeningViewAnnotationResult(
        string viewName,
        string sourceName,
        int removedAnnotationCount,
        int createdAnnotationCount,
        IReadOnlyList<string>? messages = null)
    {
        ViewName = viewName;
        SourceName = sourceName;
        RemovedAnnotationCount = removedAnnotationCount;
        CreatedAnnotationCount = createdAnnotationCount;
        Messages = messages ?? [];
    }

    public string ViewName { get; }

    public string SourceName { get; }

    public int RemovedAnnotationCount { get; }

    public int CreatedAnnotationCount { get; }

    public IReadOnlyList<string> Messages { get; }

    public string ToDialogText()
    {
        List<string> lines =
        [
            $"Вид: {ViewName}",
            $"Проём: {SourceName}",
            $"Создано аннотаций: {CreatedAnnotationCount}",
            $"Заменено прежних аннотаций TrueBIM: {RemovedAnnotationCount}"
        ];

        if (Messages.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(Messages.Select(message => $"- {message}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
