namespace TrueBIM.App.Modules.SheetNumbering.Rules;

public sealed record NumberingRules(
    string Prefix,
    string Suffix,
    int StartNumber,
    int Increment,
    int Padding)
{
    public string FormatNumber(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex), "Index must be zero or greater.");
        }

        if (Increment == 0)
        {
            throw new InvalidOperationException("Increment must not be zero.");
        }

        if (Padding < 0)
        {
            throw new InvalidOperationException("Padding must be zero or greater.");
        }

        int number = StartNumber + (zeroBasedIndex * Increment);
        string numericPart = Padding == 0
            ? number.ToString()
            : number.ToString("D" + Padding);

        return Prefix + numericPart + Suffix;
    }
}
