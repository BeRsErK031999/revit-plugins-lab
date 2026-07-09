using System.IO;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyTypeCatalogReader
{
    public FamilyTypeCatalogInfo ReadForFamily(string familyFilePath)
    {
        string normalizedFamilyPath = FamilyPathNormalizer.Normalize(familyFilePath);
        if (string.IsNullOrWhiteSpace(normalizedFamilyPath))
        {
            return new FamilyTypeCatalogInfo();
        }

        string catalogPath = Path.ChangeExtension(normalizedFamilyPath, ".txt");
        if (!File.Exists(catalogPath))
        {
            return new FamilyTypeCatalogInfo();
        }

        return new FamilyTypeCatalogInfo
        {
            Path = FamilyPathNormalizer.Normalize(catalogPath),
            TypeNames = ReadTypeNames(catalogPath).ToList()
        };
    }

    public IReadOnlyList<string> ReadTypeNames(string catalogPath)
    {
        if (!File.Exists(catalogPath))
        {
            return [];
        }

        List<string> typeNames = [];
        HashSet<string> seenNames = new(StringComparer.CurrentCultureIgnoreCase);
        bool headerSkipped = false;
        using StreamReader reader = new(catalogPath, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } rawLine)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            if (!headerSkipped)
            {
                headerSkipped = true;
                continue;
            }

            string typeName = ReadFirstField(line).Trim();
            if (typeName.Length == 0 || !seenNames.Add(typeName))
            {
                continue;
            }

            typeNames.Add(typeName);
        }

        return typeNames
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ReadFirstField(string line)
    {
        char delimiter = SelectDelimiter(line);
        if (line.Length == 0)
        {
            return string.Empty;
        }

        bool isQuoted = line[0] == '"';
        if (!isQuoted)
        {
            int delimiterIndex = line.IndexOf(delimiter);
            return delimiterIndex < 0 ? line : line.Substring(0, delimiterIndex);
        }

        List<char> chars = [];
        for (int index = 1; index < line.Length; index++)
        {
            char character = line[index];
            if (character != '"')
            {
                chars.Add(character);
                continue;
            }

            if (index + 1 < line.Length && line[index + 1] == '"')
            {
                chars.Add('"');
                index++;
                continue;
            }

            break;
        }

        return new string(chars.ToArray());
    }

    private static char SelectDelimiter(string line)
    {
        int tabIndex = line.IndexOf('\t');
        int commaIndex = line.IndexOf(',');
        if (tabIndex >= 0 && (commaIndex < 0 || tabIndex < commaIndex))
        {
            return '\t';
        }

        return ',';
    }
}
