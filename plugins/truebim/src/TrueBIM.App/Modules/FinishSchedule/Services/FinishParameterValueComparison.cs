using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishParameterValueComparison
{
    public static bool AreEqual(string first, string second)
    {
        return string.Equals(NormalizeLineEndings(first), NormalizeLineEndings(second), StringComparison.Ordinal);
    }

    public static bool IsFormattingOnly(string previous, string next)
    {
        return !AreEqual(previous, next)
               && string.Equals(ReadableValue(previous), ReadableValue(next), StringComparison.Ordinal);
    }

    public static string ReadableValue(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string NormalizeLineEndings(string value)
    {
        // Revit trims outer whitespace (including NBSP-only lines) from stored text parameters.
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
