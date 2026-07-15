namespace TrueBIM.App.Modules.Lintels.Services;

public static class LintelCandidateMatcher
{
    private static readonly string[] NameFragments = ["перемыч", "lintel"];

    public static bool IsMatch(string? familyName, string? typeName, string? instanceName)
    {
        return NameFragments.Any(fragment =>
            Contains(familyName, fragment)
            || Contains(typeName, fragment)
            || Contains(instanceName, fragment));
    }

    private static bool Contains(string? value, string fragment)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value?.IndexOf(fragment, StringComparison.InvariantCultureIgnoreCase) ?? -1) >= 0;
    }
}
