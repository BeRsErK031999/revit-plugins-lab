using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public static class ParameterDataTypeResolver
{
    public static bool IsSupported(string dataType)
    {
        return TryNormalize(dataType, out _);
    }

    public static ForgeTypeId ResolveForgeTypeId(string dataType)
    {
        string normalized = NormalizeOrThrow(dataType);
        return normalized switch
        {
            "Text" => SpecTypeId.String.Text,
            "Integer" => SpecTypeId.Int.Integer,
            "Number" => SpecTypeId.Number,
            "YesNo" => SpecTypeId.Boolean.YesNo,
            "Length" => SpecTypeId.Length,
            "Area" => SpecTypeId.Area,
            "Volume" => SpecTypeId.Volume,
            _ => throw new NotSupportedException($"Data type '{dataType}' is not supported.")
        };
    }

    public static string NormalizeForDisplay(string dataType)
    {
        return TryNormalize(dataType, out string? normalized) && normalized is not null ? normalized : dataType;
    }

    private static string NormalizeOrThrow(string dataType)
    {
        if (TryNormalize(dataType, out string? normalized))
        {
            return normalized ?? string.Empty;
        }

        throw new NotSupportedException($"Data type '{dataType}' is not supported.");
    }

    private static bool TryNormalize(string dataType, out string? normalized)
    {
        string value = dataType.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        switch (value.ToLowerInvariant())
        {
            case "text":
            case "string":
            case "строка":
            case "текст":
                normalized = "Text";
                return true;
            case "integer":
            case "int":
            case "целое":
            case "целый":
                normalized = "Integer";
                return true;
            case "number":
            case "double":
            case "число":
                normalized = "Number";
                return true;
            case "yesno":
            case "yes/no":
            case "boolean":
            case "bool":
            case "данет":
                normalized = "YesNo";
                return true;
            case "length":
            case "длина":
                normalized = "Length";
                return true;
            case "area":
            case "площадь":
                normalized = "Area";
                return true;
            case "volume":
            case "объем":
            case "объём":
                normalized = "Volume";
                return true;
            default:
                normalized = null;
                return false;
        }
    }
}
