using System.Globalization;
using System.Text;
using TrueBIM.App.Modules.Lintels.Models;

namespace TrueBIM.App.Modules.Lintels.Services;

public static class LintelArtifactNameBuilder
{
    private const string Prefix = "TB_Перемычка_";
    private const int MaxIdentityLength = 110;
    private static readonly HashSet<char> InvalidNameCharacters = new("\\/:{}[]|;<>?`~*\"".ToCharArray());

    public static LintelArtifactPreview Build(LintelTypeDiagnostic type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        string token = NormalizeToken($"{type.FamilyName}_{type.TypeName}");
        string suffix = $"_{type.TypeId.ToString(CultureInfo.InvariantCulture)}";
        int availableLength = Math.Max(8, MaxIdentityLength - Prefix.Length - suffix.Length);
        if (token.Length > availableLength)
        {
            token = token.Substring(0, availableLength).TrimEnd('_', '.');
        }

        string identity = $"{Prefix}{token}{suffix}";
        return new LintelArtifactPreview(
            identity,
            $"{identity}_Боковой_1-10",
            $"{identity}.png");
    }

    private static string NormalizeToken(string value)
    {
        StringBuilder builder = new();
        foreach (char character in value.Trim())
        {
            bool isSeparator = char.IsWhiteSpace(character)
                || char.IsControl(character)
                || InvalidNameCharacters.Contains(character);
            if (isSeparator)
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }

                continue;
            }

            builder.Append(character);
        }

        string result = builder.ToString().Trim('_', '.');
        return string.IsNullOrWhiteSpace(result)
            ? "Тип"
            : result;
    }
}
