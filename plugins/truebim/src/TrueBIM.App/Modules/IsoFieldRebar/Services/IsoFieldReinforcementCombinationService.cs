using System.Globalization;
using System.Text.RegularExpressions;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldReinforcementCombinationService
{
    private static readonly Regex ComponentPattern = new(
        @"^(?:d|ø|ф)?(?<diameter>\d+(?:[\.,]\d+)?)s(?<spacing>\d+(?:[\.,]\d+)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public bool TryParse(
        string? label,
        out IsoFieldReinforcementCombination? combination,
        out string diagnostic)
    {
        combination = null;
        if (string.IsNullOrWhiteSpace(label))
        {
            diagnostic = "Подпись сочетания диаметр/шаг не распознана.";
            return false;
        }

        string normalizedLabel = label!;
        string[] tokens = normalizedLabel
            .Replace(" ", string.Empty)
            .Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            diagnostic = $"Подпись '{label}' не содержит сочетаний вида d10s200.";
            return false;
        }

        List<(double Diameter, double Spacing)> parsed = new();
        foreach (string token in tokens)
        {
            Match match = ComponentPattern.Match(token);
            if (!match.Success
                || !TryParseNumber(match.Groups["diameter"].Value, out double diameter)
                || !TryParseNumber(match.Groups["spacing"].Value, out double spacing))
            {
                diagnostic = $"Сочетание '{token}' должно иметь формат d10s200.";
                return false;
            }

            if (diameter < 4 || diameter > 50)
            {
                diagnostic = $"Диаметр {diameter:0.###} мм вне допустимого диапазона 4–50 мм.";
                return false;
            }

            if (spacing < 50 || spacing > 400)
            {
                diagnostic = $"Шаг {spacing:0.###} мм вне допустимого диапазона 50–400 мм.";
                return false;
            }

            parsed.Add((diameter, spacing));
        }

        IsoFieldRebarComponent[] components = parsed
            .Select((value, index) => new IsoFieldRebarComponent(
                value.Diameter,
                value.Spacing,
                index,
                parsed.Count))
            .ToArray();
        combination = new IsoFieldReinforcementCombination(normalizedLabel.Trim(), components);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryParseNumber(string value, out double result)
    {
        return double.TryParse(
            value.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result)
            && !double.IsNaN(result)
            && !double.IsInfinity(result);
    }
}
