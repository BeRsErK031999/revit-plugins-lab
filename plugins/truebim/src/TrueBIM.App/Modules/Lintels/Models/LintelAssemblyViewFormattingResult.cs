namespace TrueBIM.App.Modules.Lintels.Models;

public sealed record LintelAssemblyViewFormattingResult(
    bool DimensionCreated,
    bool ElevationCreated,
    bool FrameCreated,
    bool CropAdjusted,
    int RemovedAnnotationCount,
    IReadOnlyList<string> Messages)
{
    public bool ModelChanged => DimensionCreated
        || ElevationCreated
        || FrameCreated
        || CropAdjusted
        || RemovedAnnotationCount > 0;

    public int CreatedAnnotationCount => (DimensionCreated ? 1 : 0)
        + (ElevationCreated ? 1 : 0)
        + (FrameCreated ? 1 : 0);

    public string BuildSummary()
    {
        List<string> lines =
        [
            $"Оформление: создано аннотаций — {CreatedAnnotationCount}; удалено ранее созданных — {RemovedAnnotationCount}; crop — {(CropAdjusted ? "настроен" : "без изменений")}.",
            $"Линейный размер — {ResolveStatus(DimensionCreated)}; высотная отметка — {ResolveStatus(ElevationCreated)}; рамка — {ResolveStatus(FrameCreated)}."
        ];
        lines.AddRange(Messages.Where(message => !string.IsNullOrWhiteSpace(message)));
        return string.Join(Environment.NewLine, lines);
    }

    public static LintelAssemblyViewFormattingResult Failed(string message)
    {
        return new LintelAssemblyViewFormattingResult(
            false,
            false,
            false,
            false,
            0,
            [message]);
    }

    private static string ResolveStatus(bool created)
    {
        return created ? "создан" : "не создан";
    }
}
