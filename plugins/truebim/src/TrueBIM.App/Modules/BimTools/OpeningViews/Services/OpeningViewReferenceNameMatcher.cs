namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public enum OpeningViewReferenceSide
{
    Left,
    Right,
    Bottom,
    Top
}

public static class OpeningViewReferenceNameMatcher
{
    public static int Score(string? referenceName, OpeningViewReferenceSide expectedSide)
    {
        IReadOnlyList<string> tokens = Tokenize(referenceName);
        if (tokens.Count == 0 || ContainsCenterToken(tokens))
        {
            return 0;
        }

        IReadOnlyList<OpeningViewReferenceSide> matchedSides = Enum
            .GetValues(typeof(OpeningViewReferenceSide))
            .Cast<OpeningViewReferenceSide>()
            .Where(side => tokens.Any(token => MatchesSide(token, side)))
            .ToList();
        if (matchedSides.Count != 1 || matchedSides[0] != expectedSide)
        {
            return 0;
        }

        bool explicitlyNamesOpening = tokens.Any(token =>
            token.StartsWith("проем", StringComparison.Ordinal)
            || token.StartsWith("opening", StringComparison.Ordinal));
        return explicitlyNamesOpening ? 200 : 100;
    }

    private static IReadOnlyList<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        string normalized = value!.Trim().ToLowerInvariant().Replace('ё', 'е');
        string tokenSource = new(normalized
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());
        return tokenSource.Split([' '], StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool ContainsCenterToken(IEnumerable<string> tokens)
    {
        return tokens.Any(token =>
            token.StartsWith("центр", StringComparison.Ordinal)
            || token.StartsWith("center", StringComparison.Ordinal)
            || token.StartsWith("centre", StringComparison.Ordinal)
            || token == "ось"
            || token.StartsWith("axis", StringComparison.Ordinal));
    }

    private static bool MatchesSide(string token, OpeningViewReferenceSide side)
    {
        return side switch
        {
            OpeningViewReferenceSide.Left =>
                token.StartsWith("лев", StringComparison.Ordinal)
                || token == "слева"
                || token == "left",
            OpeningViewReferenceSide.Right =>
                token.StartsWith("прав", StringComparison.Ordinal)
                || token == "справа"
                || token == "right",
            OpeningViewReferenceSide.Bottom =>
                token.StartsWith("ниж", StringComparison.Ordinal)
                || token == "низ"
                || token == "bottom",
            OpeningViewReferenceSide.Top =>
                token.StartsWith("верх", StringComparison.Ordinal)
                || token == "top",
            _ => false
        };
    }
}
