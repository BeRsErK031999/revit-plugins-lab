namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Services;

public sealed class FilterNameBuilder
{
    public const string Prefix = "［TB］ ";
    private const string LegacyPrefix = "BIM_F_";

    public string Build(string parameterName, string valueDisplay)
    {
        string safeParameterName = Sanitize(parameterName, "Параметр");
        string safeValue = Sanitize(valueDisplay, "Пусто");
        return $"{Prefix}{safeParameterName} {safeValue}";
    }

    public bool IsOwnedFilterName(string? name)
    {
        return name is not null && (name.StartsWith(Prefix, StringComparison.Ordinal)
            || name.StartsWith(LegacyPrefix, StringComparison.Ordinal));
    }

    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        const string prohibited = "{}[]|;<>?`~";
        return new string(value.Trim().Select(c => char.IsControl(c) || prohibited.Contains(c) ? '_' : c).ToArray());
    }
}
