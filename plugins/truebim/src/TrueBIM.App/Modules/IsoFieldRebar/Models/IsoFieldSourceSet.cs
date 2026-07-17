namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSourceSet(
    IReadOnlyList<IsoFieldSourceFile> Files,
    IReadOnlyList<IsoFieldLayerMapping>? LayerMappings = null)
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

    public IReadOnlyList<IsoFieldLayerMapping> EffectiveLayerMappings => LayerMappings
        ?? RequiredRoles.Select(IsoFieldLayerMapping.CreateDefault).ToArray();

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
            string[] roleConflicts = Files
                .Where(file => file.RoleDetection?.Kind == IsoFieldRoleDetectionKind.Conflict)
                .Select(file =>
                    $"{file.FileName}: имя файла указывает {file.RoleDetection!.FileNameRole}, "
                    + $"а заголовок — {file.RoleDetection.HeaderRole}; выберите слой вручную.")
                .ToArray();
            messages.AddRange(roleConflicts);
            string[] missingSizeFiles = Files
                .Where(file => !file.HasValidImageSize && string.IsNullOrWhiteSpace(file.ValidationError))
                .Select(file => file.FileName)
                .ToArray();
            if (missingSizeFiles.Length > 0)
            {
                messages.Add($"Не определён размер изображений: {string.Join(", ", missingSizeFiles)}.");
            }

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

    public IReadOnlyList<string> LayerMappingValidationMessages
    {
        get
        {
            List<string> messages = new();
            IsoFieldLayerRole[] missingRoles = RequiredRoles
                .Where(role => EffectiveLayerMappings.All(mapping => mapping.Role != role))
                .ToArray();
            if (missingRoles.Length > 0)
            {
                messages.Add($"Нет назначения для слоёв: {string.Join(", ", missingRoles)}.");
            }

            IsoFieldLayerRole[] duplicateRoles = EffectiveLayerMappings
                .GroupBy(mapping => mapping.Role)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(role => role)
                .ToArray();
            if (duplicateRoles.Length > 0)
            {
                messages.Add($"Дублируются назначения слоёв: {string.Join(", ", duplicateRoles)}.");
            }

            IsoFieldLayerRole[] invalidDirectionRoles = EffectiveLayerMappings
                .Where(mapping => mapping.Direction != IsoFieldLayerMapping.ResolveDirection(mapping.Role))
                .Select(mapping => mapping.Role)
                .Distinct()
                .OrderBy(role => role)
                .ToArray();
            if (invalidDirectionRoles.Length > 0)
            {
                messages.Add($"Направление не соответствует роли: {string.Join(", ", invalidDirectionRoles)}.");
            }

            IsoFieldLayerRole[] unconfirmedRoles = EffectiveLayerMappings
                .Where(mapping => mapping.Face == IsoFieldRebarFace.Unconfirmed)
                .Select(mapping => mapping.Role)
                .Distinct()
                .OrderBy(role => role)
                .ToArray();
            if (unconfirmedRoles.Length > 0)
            {
                messages.Add($"Не подтверждена грань: {string.Join(", ", unconfirmedRoles)}.");
            }

            if (missingRoles.Length == 0
                && duplicateRoles.Length == 0
                && invalidDirectionRoles.Length == 0)
            {
                foreach (IsoFieldRebarDirection direction in Enum.GetValues(typeof(IsoFieldRebarDirection)))
                {
                    IsoFieldLayerMapping[] directionMappings = EffectiveLayerMappings
                        .Where(mapping => mapping.Direction == direction)
                        .ToArray();
                    if (directionMappings.Any(mapping => mapping.Face == IsoFieldRebarFace.Unconfirmed))
                    {
                        continue;
                    }

                    if (directionMappings.Count(mapping => mapping.Face == IsoFieldRebarFace.Bottom) != 1
                        || directionMappings.Count(mapping => mapping.Face == IsoFieldRebarFace.Top) != 1)
                    {
                        messages.Add($"Для направления {direction} назначьте ровно по одному слою на каждую из двух граней host.");
                    }
                }
            }

            return messages;
        }
    }

    public bool HasConfirmedLayerMappings => LayerMappingValidationMessages.Count == 0;

    public IsoFieldSourceFile GetFile(IsoFieldLayerRole role)
    {
        return Files.Single(file => file.Role == role);
    }

    public IsoFieldLayerMapping GetLayerMapping(IsoFieldLayerRole role)
    {
        return EffectiveLayerMappings.FirstOrDefault(mapping => mapping.Role == role)
            ?? IsoFieldLayerMapping.CreateDefault(role);
    }
}
