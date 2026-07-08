using System.Text;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;

public static class TitleBlockFormulaEvaluator
{
    public static string Evaluate(string? formula, Func<string, string?> tokenResolver)
    {
        Guard.NotNull(tokenResolver, nameof(tokenResolver));

        string formulaText = formula ?? string.Empty;
        if (formulaText.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder result = new();
        for (int index = 0; index < formulaText.Length; index++)
        {
            char current = formulaText[index];
            if (current != '{')
            {
                result.Append(current);
                continue;
            }

            int endIndex = formulaText.IndexOf('}', index + 1);
            if (endIndex < 0)
            {
                result.Append(current);
                continue;
            }

            string token = formulaText.Substring(index + 1, endIndex - index - 1).Trim();
            result.Append(string.IsNullOrWhiteSpace(token) ? string.Empty : tokenResolver(token) ?? string.Empty);
            index = endIndex;
        }

        return result.ToString();
    }
}
