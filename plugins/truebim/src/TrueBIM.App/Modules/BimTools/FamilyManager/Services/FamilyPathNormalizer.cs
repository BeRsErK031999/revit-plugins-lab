using System.IO;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public static class FamilyPathNormalizer
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path.Trim().TrimEnd('\\', '/');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return trimmed;
        }
    }

    public static IEqualityComparer<string> Comparer { get; } = StringComparer.CurrentCultureIgnoreCase;
}
