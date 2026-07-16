using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleColumnHeadingNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string[] lines = value!
            .Replace("\u00a0", " ")
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        string result = lines[0];
        string previousLine = lines[0];
        for (int index = 1; index < lines.Length; index++)
        {
            string currentLine = lines[index];
            bool joinWithoutSpace = false;
            if (IsHyphenatedLineWrap(previousLine, currentLine, out bool removeHyphen))
            {
                if (removeHyphen && result.EndsWith("-", StringComparison.Ordinal))
                {
                    result = result.Substring(0, result.Length - 1);
                }

                joinWithoutSpace = true;
            }
            else if (IsShortWordContinuation(previousLine, currentLine))
            {
                joinWithoutSpace = true;
            }

            result += joinWithoutSpace ? currentLine : $" {currentLine}";
            previousLine = currentLine;
        }

        return Regex.Replace(result, @"\s+", " ").Trim();
    }

    private static bool IsHyphenatedLineWrap(
        string previousLine,
        string currentLine,
        out bool removeHyphen)
    {
        removeHyphen = false;
        if (!previousLine.EndsWith("-", StringComparison.Ordinal)
            || currentLine.Length == 0
            || !char.IsLower(currentLine[0]))
        {
            return false;
        }

        string previousWord = GetLastWord(previousLine.Substring(0, previousLine.Length - 1));
        string continuation = GetFirstWord(currentLine);
        if (!IsLettersOnly(previousWord) || !IsLettersOnly(continuation))
        {
            return false;
        }

        // Short prefixes such as "Коли-" are PDF line-break artifacts, while
        // complete words in compounds such as "завода-изготовителя" keep the hyphen.
        removeHyphen = previousWord.Length is >= 3 and <= 4 && continuation.Length >= 4;
        return true;
    }

    private static bool IsShortWordContinuation(string previousLine, string currentLine)
    {
        return previousLine.Length >= 5
            && currentLine.Length <= 3
            && currentLine.Length > 0
            && char.IsLower(currentLine[0])
            && IsLettersOnly(previousLine)
            && IsLettersOnly(currentLine);
    }

    private static string GetLastWord(string value)
    {
        int separator = value.LastIndexOf(' ');
        return separator < 0 ? value : value.Substring(separator + 1);
    }

    private static string GetFirstWord(string value)
    {
        int separator = value.IndexOf(' ');
        return separator < 0 ? value : value.Substring(0, separator);
    }

    private static bool IsLettersOnly(string value)
    {
        return value.Length > 0 && value.All(char.IsLetter);
    }
}
