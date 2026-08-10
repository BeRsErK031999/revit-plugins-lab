using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSourceSetManifestService
{
    public const string ManifestFileSuffix = ".isofield-set.json";
    public const string DefaultManifestFileName = "isofield-source-set.isofield-set.json";
    private const string SupportedSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IsoFieldSourceSetService sourceSetService;

    public IsoFieldSourceSetManifestService(IsoFieldSourceSetService sourceSetService)
    {
        this.sourceSetService = sourceSetService ?? throw new ArgumentNullException(nameof(sourceSetService));
    }

    public static bool IsManifestPath(string filePath)
    {
        return filePath.EndsWith(ManifestFileSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public void Save(IsoFieldSourceSet sourceSet, string manifestPath)
    {
        if (sourceSet is null)
        {
            throw new ArgumentNullException(nameof(sourceSet));
        }

        if (!sourceSet.IsComplete)
        {
            throw new InvalidOperationException(
                $"Нельзя сохранить неполный комплект карт. {string.Join(" ", sourceSet.ValidationMessages)}");
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Не указан путь для сохранения комплекта карт.", nameof(manifestPath));
        }

        string fullManifestPath = Path.GetFullPath(manifestPath);
        string manifestDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("Не удалось определить папку для сохранения комплекта карт.");
        Directory.CreateDirectory(manifestDirectory);

        SourceFileContract[] files = IsoFieldSourceSet.RequiredRoles
            .Select(role => sourceSet.GetFile(role))
            .Select(file => new SourceFileContract
            {
                Path = MakeRelativePath(manifestDirectory, file.FilePath),
                Role = file.Role!.Value.ToString(),
                PixelWidth = file.PixelWidth!.Value,
                PixelHeight = file.PixelHeight!.Value,
                Sha256 = CalculateSha256(file.FilePath)
            })
            .ToArray();
        LayerMappingContract[] mappings = IsoFieldSourceSet.RequiredRoles
            .Select(sourceSet.GetLayerMapping)
            .Select(mapping => new LayerMappingContract
            {
                Role = mapping.Role.ToString(),
                Direction = mapping.Direction.ToString(),
                Face = mapping.Face.ToString()
            })
            .ToArray();
        ManifestContract contract = new()
        {
            SchemaVersion = SupportedSchemaVersion,
            Files = files,
            LayerMappings = mappings
        };

        string json = JsonSerializer.Serialize(contract, WriteOptions);
        File.WriteAllText(fullManifestPath, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public IsoFieldSourceSet Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Не указан путь к сохранённому комплекту карт.", nameof(manifestPath));
        }

        string fullManifestPath = Path.GetFullPath(manifestPath);
        string json = File.ReadAllText(fullManifestPath, Encoding.UTF8);
        ManifestContract contract;
        try
        {
            contract = JsonSerializer.Deserialize<ManifestContract>(json, ReadOptions)
                ?? throw new InvalidDataException("В сохранённом комплекте не найдены данные о картах.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Не удалось прочитать сохранённый комплект карт. Возможно, файл повреждён.", exception);
        }

        ValidateContract(contract);
        string manifestDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("Не удалось определить папку сохранённого комплекта карт.");
        SourceFileContract[] fileContracts = contract.Files!;
        string[] resolvedPaths = fileContracts
            .Select(file => ResolveStoredPath(manifestDirectory, file.Path!))
            .ToArray();
        IsoFieldSourceSet sourceSet = sourceSetService.Build(resolvedPaths);
        if (sourceSet.Files.Count != fileContracts.Length)
        {
            throw new InvalidDataException("В сохранённом комплекте одна и та же карта указана несколько раз.");
        }

        IsoFieldSourceFile[] files = sourceSet.Files
            .Select((file, index) => ApplyManifestMetadata(file, fileContracts[index]))
            .ToArray();
        IsoFieldLayerMapping[] mappings = contract.LayerMappings!
            .Select(MapLayerMapping)
            .ToArray();
        return new IsoFieldSourceSet(files, mappings);
    }

    private static void ValidateContract(ManifestContract contract)
    {
        if (!string.Equals(contract.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Версия сохранённого комплекта не поддерживается. Нужна версия {SupportedSchemaVersion}.");
        }

        if (contract.Files is null || contract.Files.Length != IsoFieldSourceSet.RequiredRoles.Count)
        {
            throw new InvalidDataException("Сохранённый комплект должен содержать ровно четыре карты.");
        }

        if (contract.LayerMappings is null || contract.LayerMappings.Length != IsoFieldSourceSet.RequiredRoles.Count)
        {
            throw new InvalidDataException("В сохранённом комплекте должны быть назначены все четыре карты.");
        }

        if (contract.Files.Any(file => string.IsNullOrWhiteSpace(file.Path)
            || string.IsNullOrWhiteSpace(file.Role)
            || string.IsNullOrWhiteSpace(file.Sha256)
            || file.PixelWidth <= 0
            || file.PixelHeight <= 0))
        {
            throw new InvalidDataException("В сохранённом комплекте не хватает сведений об одной из карт.");
        }

        IsoFieldLayerRole[] fileRoles = contract.Files
            .Select(file => ParseEnum<IsoFieldLayerRole>(file.Role!, "file role"))
            .ToArray();
        if (fileRoles.Distinct().Count() != IsoFieldSourceSet.RequiredRoles.Count)
        {
            throw new InvalidDataException("В сохранённом комплекте несколько файлов имеют одно назначение карты.");
        }

        IsoFieldLayerRole[] mappingRoles = contract.LayerMappings
            .Select(mapping => ParseEnum<IsoFieldLayerRole>(mapping.Role, "layer mapping role"))
            .ToArray();
        if (mappingRoles.Distinct().Count() != IsoFieldSourceSet.RequiredRoles.Count)
        {
            throw new InvalidDataException("В сохранённом комплекте одно назначение карты указано несколько раз.");
        }

        for (int index = 0; index < contract.LayerMappings.Length; index++)
        {
            IsoFieldRebarDirection direction = ParseEnum<IsoFieldRebarDirection>(
                contract.LayerMappings[index].Direction,
                "layer mapping direction");
            ParseEnum<IsoFieldRebarFace>(contract.LayerMappings[index].Face, "layer mapping face");
            if (direction != IsoFieldLayerMapping.ResolveDirection(mappingRoles[index]))
            {
                throw new InvalidDataException(
                    "В сохранённом комплекте направление одной из карт не соответствует её назначению.");
            }
        }
    }

    private static IsoFieldSourceFile ApplyManifestMetadata(
        IsoFieldSourceFile sourceFile,
        SourceFileContract contract)
    {
        List<string> errors = new();
        if (!string.IsNullOrWhiteSpace(sourceFile.ValidationError))
        {
            errors.Add(sourceFile.ValidationError!);
        }

        if (sourceFile.HasValidImageSize
            && (sourceFile.PixelWidth != contract.PixelWidth || sourceFile.PixelHeight != contract.PixelHeight))
        {
            errors.Add($"размер изменён: сохранено {contract.PixelWidth}×{contract.PixelHeight}, сейчас {sourceFile.ImageSizeText}");
        }

        if (File.Exists(sourceFile.FilePath))
        {
            try
            {
                if (!string.Equals(CalculateSha256(sourceFile.FilePath), contract.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("файл изменился после сохранения комплекта");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add("не удалось проверить, изменялся ли файл");
            }
        }

        return sourceFile with
        {
            Role = ParseEnum<IsoFieldLayerRole>(contract.Role!, "file role"),
            ValidationError = errors.Count == 0 ? null : string.Join("; ", errors),
            RoleDetection = new IsoFieldRoleDetection(
                IsoFieldRoleDetectionKind.Manifest,
                sourceFile.RoleDetection?.FileNameRole,
                sourceFile.RoleDetection?.HeaderRole,
                sourceFile.RoleDetection?.HeaderConfidence)
        };
    }

    private static IsoFieldLayerMapping MapLayerMapping(LayerMappingContract contract)
    {
        return new IsoFieldLayerMapping(
            ParseEnum<IsoFieldLayerRole>(contract.Role, "layer mapping role"),
            ParseEnum<IsoFieldRebarDirection>(contract.Direction, "layer mapping direction"),
            ParseEnum<IsoFieldRebarFace>(contract.Face, "layer mapping face"));
    }

    private static TEnum ParseEnum<TEnum>(string? value, string fieldName)
        where TEnum : struct
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
            && Enum.IsDefined(typeof(TEnum), parsed))
        {
            return parsed;
        }

        throw new InvalidDataException("В сохранённом комплекте найдено неизвестное назначение карты. Назначьте карты заново.");
    }

    private static string ResolveStoredPath(string manifestDirectory, string storedPath)
    {
        return Path.GetFullPath(Path.IsPathRooted(storedPath)
            ? storedPath
            : Path.Combine(manifestDirectory, storedPath));
    }

    private static string MakeRelativePath(string baseDirectory, string filePath)
    {
        Uri baseUri = new(AppendDirectorySeparator(Path.GetFullPath(baseDirectory)));
        Uri fileUri = new(Path.GetFullPath(filePath));
        if (!string.Equals(baseUri.Scheme, fileUri.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return fileUri.LocalPath;
        }

        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string CalculateSha256(string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using SHA256 algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private sealed class ManifestContract
    {
        public string? SchemaVersion { get; set; }

        public SourceFileContract[]? Files { get; set; }

        public LayerMappingContract[]? LayerMappings { get; set; }
    }

    private sealed class SourceFileContract
    {
        public string? Path { get; set; }

        public string? Role { get; set; }

        public int PixelWidth { get; set; }

        public int PixelHeight { get; set; }

        public string? Sha256 { get; set; }
    }

    private sealed class LayerMappingContract
    {
        public string? Role { get; set; }

        public string? Direction { get; set; }

        public string? Face { get; set; }
    }
}
