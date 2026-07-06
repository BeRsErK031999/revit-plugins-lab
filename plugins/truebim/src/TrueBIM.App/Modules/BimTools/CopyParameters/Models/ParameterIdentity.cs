using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Models;

public sealed record ParameterIdentity(
    string Name,
    Guid? SharedParameterGuid,
    BuiltInParameter? BuiltInParameter,
    StorageType StorageType);
