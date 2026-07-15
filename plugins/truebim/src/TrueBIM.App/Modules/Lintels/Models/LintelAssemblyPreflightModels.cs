namespace TrueBIM.App.Modules.Lintels.Models;

public enum LintelAssemblyPreflightStatus
{
    Ready,
    AlreadyExists,
    Blocked
}

public sealed record LintelAssemblyPreflightItem(
    long TypeId,
    string FamilyName,
    string TypeName,
    string AssemblyName,
    long RepresentativeElementId,
    IReadOnlyList<long> MemberElementIds,
    long? NamingCategoryId,
    LintelAssemblyPreflightStatus Status,
    string Message)
{
    public int MemberCount => MemberElementIds.Count;

    public string MemberIdsDisplay => string.Join(", ", MemberElementIds);

    public string NamingCategoryDisplay => NamingCategoryId?.ToString() ?? "—";

    public string StatusDisplay => Status switch
    {
        LintelAssemblyPreflightStatus.Ready => "Готово",
        LintelAssemblyPreflightStatus.AlreadyExists => "Уже существует",
        _ => "Заблокировано"
    };
}

public sealed record LintelAssemblyPreflightResult(
    IReadOnlyList<LintelAssemblyPreflightItem> Items)
{
    public int ReadyCount => Items.Count(item => item.Status == LintelAssemblyPreflightStatus.Ready);

    public int ExistingCount => Items.Count(item => item.Status == LintelAssemblyPreflightStatus.AlreadyExists);

    public int BlockedCount => Items.Count(item => item.Status == LintelAssemblyPreflightStatus.Blocked);

    public string BuildSummary()
    {
        return $"Проверено типоразмеров: {Items.Count}. Готово к созданию: {ReadyCount}; уже существует: {ExistingCount}; заблокировано: {BlockedCount}.{Environment.NewLine}Preflight не открывал транзакцию и не изменял модель Revit.";
    }
}
