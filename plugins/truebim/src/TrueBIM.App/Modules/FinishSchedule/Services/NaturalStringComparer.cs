namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
            {
                int comparison = CompareNumber(
                    left,
                    ref leftIndex,
                    right,
                    ref rightIndex);
                if (comparison != 0)
                {
                    return comparison;
                }

                continue;
            }

            char leftCharacter = char.ToUpperInvariant(left[leftIndex]);
            char rightCharacter = char.ToUpperInvariant(right[rightIndex]);
            int characterComparison = leftCharacter.CompareTo(rightCharacter);
            if (characterComparison != 0)
            {
                return characterComparison;
            }

            leftIndex++;
            rightIndex++;
        }

        int lengthComparison = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        return lengthComparison != 0
            ? lengthComparison
            : StringComparer.Ordinal.Compare(left, right);
    }

    private static int CompareNumber(
        string left,
        ref int leftIndex,
        string right,
        ref int rightIndex)
    {
        int leftStart = leftIndex;
        int rightStart = rightIndex;
        while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
        {
            leftIndex++;
        }

        while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
        {
            rightIndex++;
        }

        int leftSignificant = SkipLeadingZeros(left, leftStart, leftIndex);
        int rightSignificant = SkipLeadingZeros(right, rightStart, rightIndex);
        int leftSignificantLength = leftIndex - leftSignificant;
        int rightSignificantLength = rightIndex - rightSignificant;
        int significantLengthComparison = leftSignificantLength.CompareTo(rightSignificantLength);
        if (significantLengthComparison != 0)
        {
            return significantLengthComparison;
        }

        for (int index = 0; index < leftSignificantLength; index++)
        {
            int digitComparison = left[leftSignificant + index]
                .CompareTo(right[rightSignificant + index]);
            if (digitComparison != 0)
            {
                return digitComparison;
            }
        }

        int leftRunLength = leftIndex - leftStart;
        int rightRunLength = rightIndex - rightStart;
        return leftRunLength.CompareTo(rightRunLength);
    }

    private static int SkipLeadingZeros(string value, int start, int end)
    {
        int index = start;
        while (index < end - 1 && value[index] == '0')
        {
            index++;
        }

        return index;
    }
}
