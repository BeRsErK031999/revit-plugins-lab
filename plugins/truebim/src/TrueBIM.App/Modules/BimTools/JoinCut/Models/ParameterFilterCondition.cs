using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.JoinCut.Models;

public sealed class ParameterFilterCondition
{
    public string? ParameterName { get; set; }

    public Guid? ParameterGuid { get; set; }

    public BuiltInParameter? BuiltInParameter { get; set; }

    public ParameterCompareOperator Operator { get; set; } = ParameterCompareOperator.Equals;

    public string? Value { get; set; }
}
