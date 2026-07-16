namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSourceSet(IReadOnlyList<IsoFieldSourceFile> Files)
{
    public static IReadOnlyList<IsoFieldLayerRole> RequiredRoles { get; } =
    [
        IsoFieldLayerRole.As1X,
        IsoFieldLayerRole.As2X,
        IsoFieldLayerRole.As3Y,
        IsoFieldLayerRole.As4Y
    ];

    public IReadOnlyList<IsoFieldLayerRole> MissingRoles => RequiredRoles
        .Where(role => Files.All(file => file.Role != role))
        .ToArray();

    public IReadOnlyList<IsoFieldLayerRole> DuplicateRoles => Files
        .Where(file => file.Role.HasValue)
        .GroupBy(file => file.Role!.Value)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .OrderBy(role => role)
        .ToArray();

    public bool HasConsistentImageSize
    {
        get
        {
            IsoFieldSourceFile[] validFiles = Files
                .Where(file => file.HasValidImageSize)
                .ToArray();
            return validFiles.Length == Files.Count
                && validFiles
                    .Select(file => (file.PixelWidth, file.PixelHeight))
                    .Distinct()
                    .Count() <= 1;
        }
    }

    public IReadOnlyList<string> ValidationMessages
    {
        get
        {
            List<string> messages = new();
            if (Files.Count != RequiredRoles.Count)
            {
                messages.Add($"Нужно выбрать ровно 4 изображения; выбрано: {Files.Count}.");
            }

            string[] fileErrors = Files
                .Where(file => !string.IsNullOrWhiteSpace(file.ValidationError))
                .Select(file => $"{file.FileName}: {file.ValidationError}")
                .ToArray();
            messages.AddRange(fileErrors);

            if (MissingRoles.Count > 0)
            {
                messages.Add($"Не назначены слои: {string.Join(", ", MissingRoles)}.");
            }

            if (DuplicateRoles.Count > 0)
            {
                messages.Add($"Дублируются слои: {string.Join(", ", DuplicateRoles)}.");
            }

            if (Files.Count > 0 && Files.All(file => file.HasValidImageSize) && !HasConsistentImageSize)
            {
                messages.Add("Размеры изображений различаются. Проверьте, что карты экспортированы в одном масштабе.");
            }

            return messages;
        }
    }

    public bool IsComplete => Files.Count == RequiredRoles.Count && ValidationMessages.Count == 0;

    public IsoFieldSourceFile GetFile(IsoFieldLayerRole role)
    {
        return Files.Single(file => file.Role == role);
    }
}
