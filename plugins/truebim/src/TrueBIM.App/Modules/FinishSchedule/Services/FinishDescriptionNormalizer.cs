using System.Text;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishDescriptionNormalizer
{
    public const string MissingDescriptionDisplay = "[Описание не задано]";

    public NormalizedFinishDescription Normalize(string? value)
    {
        string displayValue = CollapseWhitespace(value);
        if (displayValue.Length == 0)
        {
            displayValue = MissingDescriptionDisplay;
        }

        return new NormalizedFinishDescription(
            displayValue,
            displayValue.ToUpperInvariant());
    }

    public static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmedValue = value!.Trim();
        StringBuilder builder = new(trimmedValue.Length);
        bool pendingSpace = false;
        foreach (char character in trimmedValue)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
