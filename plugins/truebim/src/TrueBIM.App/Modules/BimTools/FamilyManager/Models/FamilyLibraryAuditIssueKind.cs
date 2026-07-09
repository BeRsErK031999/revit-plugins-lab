namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public enum FamilyLibraryAuditIssueKind
{
    MissingFile,
    StaleMetadata,
    DuplicateName,
    DuplicateSignature,
    DuplicateRelativePath,
    EmptyCategory,
    MissingTypes
}
