using System.Text;
using TrueBIM.App.Modules.SharedParameters.Models;

namespace TrueBIM.App.Modules.SharedParameters.Services;

public sealed class FamilyFormulaDependencyParser
{
    public DetectionConfidence ClassifyReference(string? formula, string? parameterName)
    {
        if (string.IsNullOrWhiteSpace(formula) || string.IsNullOrWhiteSpace(parameterName))
        {
            return DetectionConfidence.Unsupported;
        }

        string formulaValue = formula ?? string.Empty;
        string target = (parameterName ?? string.Empty).Trim();
        foreach (FormulaToken token in Tokenize(formulaValue))
        {
            if (string.Equals(token.Value, target, StringComparison.OrdinalIgnoreCase))
            {
                return DetectionConfidence.Exact;
            }
        }

        return formulaValue.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0
            ? DetectionConfidence.Probable
            : DetectionConfidence.Unsupported;
    }

    public IReadOnlyList<string> ExtractParameterReferences(
        string? formula,
        IReadOnlyList<string> knownParameterNames)
    {
        if (string.IsNullOrWhiteSpace(formula) || knownParameterNames is null)
        {
            return [];
        }

        HashSet<string> references = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<FormulaToken> tokens = Tokenize(formula ?? string.Empty);
        foreach (string knownName in knownParameterNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (tokens.Any(token => string.Equals(token.Value, knownName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                references.Add(knownName);
            }
        }

        return references.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IReadOnlyList<FormulaToken> Tokenize(string formula)
    {
        List<FormulaToken> tokens = [];
        StringBuilder current = new();
        bool insideBrackets = false;

        void Flush()
        {
            string value = current.ToString().Trim();
            current.Clear();
            if (value.Length > 0)
            {
                tokens.Add(new FormulaToken(value, insideBrackets));
            }
        }

        foreach (char character in formula)
        {
            if (character == '[')
            {
                Flush();
                insideBrackets = true;
                continue;
            }

            if (character == ']')
            {
                Flush();
                insideBrackets = false;
                continue;
            }

            if (insideBrackets || IsIdentifierCharacter(character))
            {
                current.Append(character);
                continue;
            }

            Flush();
        }

        Flush();
        return tokens;
    }

    private static bool IsIdentifierCharacter(char character)
    {
        return char.IsLetterOrDigit(character)
            || character is '_' or ' ' or '.' or '-' or 'ё' or 'Ё';
    }

    private sealed record FormulaToken(string Value, bool WasBracketed);
}
