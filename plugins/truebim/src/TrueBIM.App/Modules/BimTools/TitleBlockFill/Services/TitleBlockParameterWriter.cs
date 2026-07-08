using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;

public sealed class TitleBlockParameterWriter
{
    public string Read(Parameter parameter)
    {
        Guard.NotNull(parameter, nameof(parameter));

        return parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
    }

    public bool TryWrite(Parameter parameter, string value, out string message)
    {
        Guard.NotNull(parameter, nameof(parameter));

        if (parameter.IsReadOnly)
        {
            message = "Параметр доступен только для чтения.";
            return false;
        }

        try
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    parameter.Set(value ?? string.Empty);
                    message = "Значение записано.";
                    return true;
                case StorageType.Integer:
                    if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out int integerValue)
                        || int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out integerValue))
                    {
                        parameter.Set(integerValue);
                        message = "Значение записано.";
                        return true;
                    }

                    message = "Значение не является целым числом.";
                    return false;
                case StorageType.Double:
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double doubleValue)
                        || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out doubleValue))
                    {
                        parameter.Set(doubleValue);
                        message = "Значение записано.";
                        return true;
                    }

                    message = "Значение не является числом.";
                    return false;
                default:
                    message = $"Тип параметра {parameter.StorageType} не поддержан в MVP.";
                    return false;
            }
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.InvalidOperationException or ArgumentException)
        {
            message = exception.Message;
            return false;
        }
    }
}
