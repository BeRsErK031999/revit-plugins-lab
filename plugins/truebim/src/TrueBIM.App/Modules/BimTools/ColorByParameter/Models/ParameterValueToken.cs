namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed class ParameterValueToken : IEquatable<ParameterValueToken>
{
    public const string EmptyDisplayValue = "<Пусто / Не заполнено>";

    private ParameterValueToken(
        ParameterValueKind kind,
        string displayValue,
        string? stringValue,
        int? integerValue,
        double? doubleValue,
        long? elementIdValue)
    {
        Kind = kind;
        DisplayValue = string.IsNullOrWhiteSpace(displayValue) ? EmptyDisplayValue : displayValue;
        StringValue = stringValue;
        IntegerValue = integerValue;
        DoubleValue = doubleValue;
        ElementIdValue = elementIdValue;
    }

    public ParameterValueKind Kind { get; }

    public string DisplayValue { get; }

    public string? StringValue { get; }

    public int? IntegerValue { get; }

    public double? DoubleValue { get; }

    public long? ElementIdValue { get; }

    public bool IsEmpty => Kind == ParameterValueKind.Empty;

    public static ParameterValueToken Empty()
    {
        return new ParameterValueToken(ParameterValueKind.Empty, EmptyDisplayValue, null, null, null, null);
    }

    public static ParameterValueToken FromString(string value, string displayValue)
    {
        return new ParameterValueToken(ParameterValueKind.String, displayValue, value, null, null, null);
    }

    public static ParameterValueToken FromInteger(int value, string displayValue)
    {
        return new ParameterValueToken(ParameterValueKind.Integer, displayValue, null, value, null, null);
    }

    public static ParameterValueToken FromDouble(double value, string displayValue)
    {
        return new ParameterValueToken(ParameterValueKind.Double, displayValue, null, null, value, null);
    }

    public static ParameterValueToken FromElementId(long value, string displayValue)
    {
        return new ParameterValueToken(ParameterValueKind.ElementId, displayValue, null, null, null, value);
    }

    public bool Equals(ParameterValueToken? other)
    {
        if (other is null)
        {
            return false;
        }

        return Kind == other.Kind
            && string.Equals(StringValue, other.StringValue, StringComparison.Ordinal)
            && IntegerValue == other.IntegerValue
            && DoubleValue == other.DoubleValue
            && ElementIdValue == other.ElementIdValue
            && string.Equals(DisplayValue, other.DisplayValue, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ParameterValueToken);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + Kind.GetHashCode();
            hash = (hash * 31) + (StringValue?.GetHashCode() ?? 0);
            hash = (hash * 31) + IntegerValue.GetHashCode();
            hash = (hash * 31) + DoubleValue.GetHashCode();
            hash = (hash * 31) + ElementIdValue.GetHashCode();
            hash = (hash * 31) + DisplayValue.GetHashCode();
            return hash;
        }
    }
}
