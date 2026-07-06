namespace TrueBIM.App.Modules.BimTools.ParaManager.Models;

public enum ParameterImportStatus
{
    Empty,
    Invalid,
    DuplicateInFile,
    WillCreate,
    WillUpdate,
    Created,
    Updated,
    Skipped,
    Failed
}
