using Autodesk.Revit.DB;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Models;

public sealed record ParameterValueSnapshot(
    StorageType StorageType,
    string? StringValue,
    int? IntegerValue,
    double? DoubleValue,
    ElementId? ElementIdValue,
    string DisplayValue)
{
    public static ParameterValueSnapshot? TryCreate(Parameter parameter)
    {
        try
        {
            return parameter.StorageType switch
            {
                StorageType.String => CreateString(parameter),
                StorageType.Integer => new ParameterValueSnapshot(
                    StorageType.Integer,
                    null,
                    parameter.AsInteger(),
                    null,
                    null,
                    GetDisplayValue(parameter, parameter.AsInteger().ToString(System.Globalization.CultureInfo.CurrentCulture))),
                StorageType.Double => new ParameterValueSnapshot(
                    StorageType.Double,
                    null,
                    null,
                    parameter.AsDouble(),
                    null,
                    GetDisplayValue(parameter, parameter.AsDouble().ToString("G", System.Globalization.CultureInfo.CurrentCulture))),
                StorageType.ElementId => CreateElementId(parameter),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    public void ApplyTo(Parameter parameter)
    {
        switch (StorageType)
        {
            case StorageType.String:
                parameter.Set(StringValue ?? string.Empty);
                break;
            case StorageType.Integer:
                parameter.Set(IntegerValue.GetValueOrDefault());
                break;
            case StorageType.Double:
                parameter.Set(DoubleValue.GetValueOrDefault());
                break;
            case StorageType.ElementId:
                if (ElementIdValue is null)
                {
                    throw new InvalidOperationException("ElementId value is empty.");
                }

                parameter.Set(ElementIdValue);
                break;
            default:
                throw new NotSupportedException($"Storage type '{StorageType}' is not supported.");
        }
    }

    private static ParameterValueSnapshot? CreateString(Parameter parameter)
    {
        string? value = parameter.AsString();
        if (value is null)
        {
            return null;
        }

        return new ParameterValueSnapshot(
            StorageType.String,
            value,
            null,
            null,
            null,
            GetDisplayValue(parameter, value));
    }

    private static ParameterValueSnapshot? CreateElementId(Parameter parameter)
    {
        ElementId value = parameter.AsElementId();
        if (value == ElementId.InvalidElementId)
        {
            return null;
        }

        return new ParameterValueSnapshot(
            StorageType.ElementId,
            null,
            null,
            null,
            value,
            GetDisplayValue(parameter, RevitElementIds.GetValue(value).ToString(System.Globalization.CultureInfo.CurrentCulture)));
    }

    private static string GetDisplayValue(Parameter parameter, string fallback)
    {
        string? valueString = parameter.AsValueString();
        return string.IsNullOrWhiteSpace(valueString) ? fallback : valueString;
    }
}
