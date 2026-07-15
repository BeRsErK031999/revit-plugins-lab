namespace TrueBIM.App.Modules.Lintels.Models;

public enum LintelAssemblyViewCreationStatus
{
    Created,
    AlreadyExists,
    Blocked,
    Failed
}

public sealed record LintelAssemblyViewCreationResult(
    LintelAssemblyViewCreationStatus Status,
    string AssemblyName,
    string ViewName,
    long? ViewElementId,
    string Message,
    LintelAssemblyViewFormattingResult? Formatting = null)
{
    public bool ModelChanged => Status == LintelAssemblyViewCreationStatus.Created
        || Formatting?.ModelChanged == true;

    public string StatusDisplay => Status switch
    {
        LintelAssemblyViewCreationStatus.Created => "Создано",
        LintelAssemblyViewCreationStatus.AlreadyExists => "Уже существует",
        LintelAssemblyViewCreationStatus.Blocked => "Заблокировано",
        _ => "Ошибка"
    };

    public string BuildSummary()
    {
        string elementId = ViewElementId is > 0
            ? $" ElementId: {ViewElementId}."
            : string.Empty;
        string summary = $"{StatusDisplay}: {ViewName}.{elementId}{Environment.NewLine}{Message}";
        return Formatting is null
            ? summary
            : $"{summary}{Environment.NewLine}{Environment.NewLine}{Formatting.BuildSummary()}";
    }
}
