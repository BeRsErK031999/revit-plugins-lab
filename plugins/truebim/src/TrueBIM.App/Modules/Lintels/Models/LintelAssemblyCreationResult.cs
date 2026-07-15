namespace TrueBIM.App.Modules.Lintels.Models;

public enum LintelAssemblyCreationStatus
{
    Created,
    AlreadyExists,
    Blocked,
    Failed
}

public sealed record LintelAssemblyCreationResult(
    LintelAssemblyCreationStatus Status,
    string AssemblyName,
    long? AssemblyElementId,
    string Message)
{
    public bool ModelChanged => Status == LintelAssemblyCreationStatus.Created;

    public string StatusDisplay => Status switch
    {
        LintelAssemblyCreationStatus.Created => "Создано",
        LintelAssemblyCreationStatus.AlreadyExists => "Уже существует",
        LintelAssemblyCreationStatus.Blocked => "Заблокировано",
        _ => "Ошибка"
    };

    public string BuildSummary()
    {
        string elementId = AssemblyElementId is > 0
            ? $" ElementId: {AssemblyElementId}."
            : string.Empty;
        return $"{StatusDisplay}: {AssemblyName}.{elementId}{Environment.NewLine}{Message}";
    }
}
