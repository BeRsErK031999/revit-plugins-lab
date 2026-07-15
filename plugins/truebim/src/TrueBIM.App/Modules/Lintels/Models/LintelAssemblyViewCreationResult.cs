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
    string Message)
{
    public bool ModelChanged => Status == LintelAssemblyViewCreationStatus.Created;

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
        return $"{StatusDisplay}: {ViewName}.{elementId}{Environment.NewLine}{Message}";
    }
}
