namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed class ColorRuleRow
{
    public ColorRuleRow(ParameterValueToken value, ColorSwatch color)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        SetColor(color);
        IsSelected = true;
    }

    public ParameterValueToken Value { get; }

    public bool IsSelected { get; set; }

    public byte Red { get; private set; }

    public byte Green { get; private set; }

    public byte Blue { get; private set; }

    public string DisplayValue => Value.DisplayValue;

    public string ColorHex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public void SetColor(ColorSwatch color)
    {
        Red = color.Red;
        Green = color.Green;
        Blue = color.Blue;
    }
}
