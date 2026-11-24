namespace TrueBIM.App.Modules.SystemDesignation;

public static class SystemAbbreviations
{
    public static string Format(IEnumerable<string> values) => string.Join(", ", values
        .Select(value => value.Trim()).Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
}
