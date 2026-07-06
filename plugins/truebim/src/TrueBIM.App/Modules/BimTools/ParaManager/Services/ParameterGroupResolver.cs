using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public static class ParameterGroupResolver
{
    public static bool IsSupported(string groupUnder)
    {
        return TryNormalize(groupUnder, out _);
    }

#if REVIT2022_OR_GREATER
    public static ForgeTypeId ResolveForgeTypeId(string groupUnder)
    {
        string normalized = NormalizeOrThrow(groupUnder);
        return normalized switch
        {
            "IdentityData" => GroupTypeId.IdentityData,
            "Text" => GroupTypeId.Text,
            "Dimensions" => GroupTypeId.Geometry,
            "Data" => GroupTypeId.Data,
            "General" => GroupTypeId.General,
            "Other" => GroupTypeId.Data,
            _ => throw new NotSupportedException($"Parameter group '{groupUnder}' is not supported.")
        };
    }
#else
    public static BuiltInParameterGroup ResolveBuiltInParameterGroup(string groupUnder)
    {
        string normalized = NormalizeOrThrow(groupUnder);
        return normalized switch
        {
            "IdentityData" => BuiltInParameterGroup.PG_IDENTITY_DATA,
            "Text" => BuiltInParameterGroup.PG_TEXT,
            "Dimensions" => BuiltInParameterGroup.PG_GEOMETRY,
            "Data" => BuiltInParameterGroup.PG_DATA,
            "General" => BuiltInParameterGroup.PG_GENERAL,
            "Other" => BuiltInParameterGroup.INVALID,
            _ => throw new NotSupportedException($"Parameter group '{groupUnder}' is not supported.")
        };
    }
#endif

    public static string NormalizeForDisplay(string groupUnder)
    {
        return TryNormalize(groupUnder, out string? normalized) && normalized is not null ? normalized : groupUnder;
    }

    private static string NormalizeOrThrow(string groupUnder)
    {
        if (TryNormalize(groupUnder, out string? normalized))
        {
            return normalized ?? string.Empty;
        }

        throw new NotSupportedException($"Parameter group '{groupUnder}' is not supported.");
    }

    private static bool TryNormalize(string groupUnder, out string? normalized)
    {
        string value = groupUnder.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        switch (value.ToLowerInvariant())
        {
            case "":
            case "other":
            case "прочее":
                normalized = "Other";
                return true;
            case "identitydata":
            case "identity":
            case "данныеидентификации":
            case "идентификация":
                normalized = "IdentityData";
                return true;
            case "text":
            case "текст":
                normalized = "Text";
                return true;
            case "dimensions":
            case "dimension":
            case "geometry":
            case "размеры":
            case "геометрия":
                normalized = "Dimensions";
                return true;
            case "data":
            case "данные":
                normalized = "Data";
                return true;
            case "general":
            case "общие":
                normalized = "General";
                return true;
            default:
                normalized = null;
                return false;
        }
    }
}
