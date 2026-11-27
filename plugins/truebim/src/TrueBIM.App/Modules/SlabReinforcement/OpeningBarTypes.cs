using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal static class OpeningBarTypes
{
    // Project type names may contain spaces and a final dot in the parenthesized prefix.
    private static string Key(string name) => Regex.Replace(name.ToUpperInvariant(), @"[\s.]", "");
    internal static bool IsStraight(string name)
    {
        string key = Key(name);
        return key.Contains("(ППФОН)") && key.Contains("ПМ");
    }
    internal static bool IsBend(string name)
    {
        string key = Key(name);
        return key.Contains("(ППОБР)") && !key.Contains("ПМ");
    }
}
