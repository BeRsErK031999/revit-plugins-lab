using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SharedParameters.Models;

namespace TrueBIM.App.Modules.SharedParameters.Revit;

public interface ISharedParameterVersionAdapter
{
    string GetDataTypeName(Definition definition);

    string GetParameterGroupName(Definition definition);

    FamilyParameter? FindFamilyParameter(FamilyManager familyManager, Guid guid);

    string GetFamilyParameterDataTypeName(FamilyParameter parameter);

    string GetFamilyParameterGroupName(FamilyParameter parameter);

    IReadOnlyList<FilterRuleDescriptor> ExtractFilterRules(
        Document document,
        ElementFilter filter,
        long targetParameterId);
}
