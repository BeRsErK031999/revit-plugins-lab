namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyLibraryAuditIssue
{
    public FamilyLibraryAuditSeverity Severity { get; set; }

    public FamilyLibraryAuditIssueKind Kind { get; set; }

    public string FamilyName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string GroupKey { get; set; } = string.Empty;

    public int RelatedCount { get; set; } = 1;

    public string SeverityDisplay => Severity switch
    {
        FamilyLibraryAuditSeverity.Error => "Ошибка",
        FamilyLibraryAuditSeverity.Warning => "Риск",
        _ => "Инфо"
    };

    public string KindDisplay => Kind switch
    {
        FamilyLibraryAuditIssueKind.MissingFile => "Файл отсутствует",
        FamilyLibraryAuditIssueKind.StaleMetadata => "Cache устарел",
        FamilyLibraryAuditIssueKind.DuplicateName => "Дубль имени",
        FamilyLibraryAuditIssueKind.DuplicateSignature => "Дубль файла",
        FamilyLibraryAuditIssueKind.DuplicateRelativePath => "Дубль пути",
        FamilyLibraryAuditIssueKind.EmptyCategory => "Категория",
        FamilyLibraryAuditIssueKind.MissingTypes => "Типы",
        FamilyLibraryAuditIssueKind.BackupFile => "Backup .rfa",
        _ => Kind.ToString()
    };

    public string CountDisplay => RelatedCount <= 1
        ? "-"
        : RelatedCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string PathDisplay => string.IsNullOrWhiteSpace(FilePath) ? "-" : FilePath;
}
