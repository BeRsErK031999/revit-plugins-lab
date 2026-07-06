using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.JoinCut.Models;

public sealed class ElementFilterDefinition
{
    public List<BuiltInCategory> Categories { get; set; } = [];

    public List<ParameterFilterCondition> ParameterConditions { get; set; } = [];

    public FilterLogicalOperator CategoryAndParameterOperator { get; set; } = FilterLogicalOperator.And;
}
