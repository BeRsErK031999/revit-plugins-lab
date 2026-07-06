using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.CopyParameters.Models;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Services;

public sealed class ParameterCompatibilityService
{
    private static readonly string[] RiskyNameFragments =
    [
        "level",
        "family",
        "type",
        "constraint",
        "offset",
        "уровень",
        "семейство",
        "тип",
        "смещение",
        "ограничение"
    ];

    public bool CanCollect(Parameter parameter)
    {
        if (parameter.Definition is null || string.IsNullOrWhiteSpace(parameter.Definition.Name))
        {
            return false;
        }

        if (parameter.IsReadOnly || parameter.StorageType == StorageType.None)
        {
            return false;
        }

        return ParameterValueSnapshot.TryCreate(parameter) is not null;
    }

    public string BuildWarning(Parameter parameter, ParameterSourceKind sourceKind)
    {
        List<string> warnings = new();
        string parameterName = parameter.Definition?.Name ?? string.Empty;
        if (sourceKind == ParameterSourceKind.Type)
        {
            warnings.Add("Параметр типа: изменение затронет тип элемента-получателя.");
        }

        if (parameter.StorageType == StorageType.ElementId)
        {
            warnings.Add("ElementId: копируется ссылка на объект текущего документа.");
        }

        if (RiskyNameFragments.Any(fragment => parameterName.IndexOf(fragment, StringComparison.CurrentCultureIgnoreCase) >= 0))
        {
            warnings.Add("Проверьте вручную: параметр может влиять на тип, уровень или геометрию.");
        }

        return string.Join(" ", warnings);
    }

    public bool CanWrite(Parameter targetParameter, ParameterValueSnapshot value, out string reason)
    {
        if (targetParameter.IsReadOnly)
        {
            reason = "параметр только для чтения";
            return false;
        }

        if (targetParameter.StorageType != value.StorageType)
        {
            reason = "несовпадение типа значения";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
