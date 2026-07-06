namespace TrueBIM.App.Modules.BimTools.ParaManager.Models;

public sealed record SharedParameterDefinitionInfo(
    string Name,
    string GroupName,
    string DataTypeDisplay,
    Guid Guid,
    bool WasCreated);
