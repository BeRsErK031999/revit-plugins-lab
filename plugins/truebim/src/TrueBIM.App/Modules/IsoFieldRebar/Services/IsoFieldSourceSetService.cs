using System.IO;
using System.Windows.Media.Imaging;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSourceSetService
{
    private readonly IsoFieldHeaderRoleRecognizer headerRoleRecognizer;

    public IsoFieldSourceSetService()
        : this(new IsoFieldHeaderRoleRecognizer())
    {
    }

    public IsoFieldSourceSetService(IsoFieldHeaderRoleRecognizer headerRoleRecognizer)
    {
        this.headerRoleRecognizer = headerRoleRecognizer
            ?? throw new ArgumentNullException(nameof(headerRoleRecognizer));
    }

    public IsoFieldSourceSet Build(IEnumerable<string> filePaths)
    {
        if (filePaths is null)
        {
            throw new ArgumentNullException(nameof(filePaths));
        }

        IsoFieldSourceFile[] files = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateSourceFile)
            .ToArray();

        return new IsoFieldSourceSet(files);
    }

    public IsoFieldSourceSet AssignFace(
        IsoFieldSourceSet sourceSet,
        IsoFieldLayerRole role,
        IsoFieldRebarFace face)
    {
        if (sourceSet is null)
        {
            throw new ArgumentNullException(nameof(sourceSet));
        }

        IsoFieldLayerMapping[] mappings = IsoFieldSourceSet.RequiredRoles
            .Select(requiredRole => requiredRole == role
                ? sourceSet.GetLayerMapping(requiredRole) with { Face = face }
                : sourceSet.GetLayerMapping(requiredRole))
            .ToArray();
        return new IsoFieldSourceSet(sourceSet.Files, mappings);
    }

    public IsoFieldSourceSet AssignRole(
        IsoFieldSourceSet sourceSet,
        string filePath,
        IsoFieldLayerRole role)
    {
        if (sourceSet is null)
        {
            throw new ArgumentNullException(nameof(sourceSet));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Source file path is required.", nameof(filePath));
        }

        bool found = false;
        IsoFieldSourceFile[] files = sourceSet.Files
            .Select(file =>
            {
                if (!string.Equals(file.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }

                found = true;
                return file with
                {
                    Role = role,
                    RoleDetection = new IsoFieldRoleDetection(
                        IsoFieldRoleDetectionKind.Manual,
                        file.RoleDetection?.FileNameRole,
                        file.RoleDetection?.HeaderRole,
                        file.RoleDetection?.HeaderConfidence)
                };
            })
            .ToArray();

        if (!found)
        {
            throw new ArgumentException("Source file is not part of the selected set.", nameof(filePath));
        }

        return new IsoFieldSourceSet(files, sourceSet.LayerMappings);
    }

    public static IsoFieldLayerRole? DetectRole(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        IsoFieldLayerRole[] matches = IsoFieldSourceSet.RequiredRoles
            .Where(role => fileName.IndexOf(role.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private IsoFieldSourceFile CreateSourceFile(string filePath)
    {
        IsoFieldLayerRole? fileNameRole = DetectRole(filePath);
        try
        {
            if (!File.Exists(filePath))
            {
                return new IsoFieldSourceFile(
                    filePath,
                    fileNameRole,
                    null,
                    null,
                    "файл не найден",
                    ResolveRoleDetection(fileNameRole, IsoFieldHeaderRoleRecognition.NotDetected));
            }

            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            IsoFieldHeaderRoleRecognition headerRecognition;
            try
            {
                headerRecognition = headerRoleRecognizer.Recognize(frame);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                headerRecognition = IsoFieldHeaderRoleRecognition.NotDetected;
            }

            IsoFieldRoleDetection roleDetection = ResolveRoleDetection(fileNameRole, headerRecognition);
            IsoFieldLayerRole? role = roleDetection.Kind == IsoFieldRoleDetectionKind.Conflict
                ? null
                : headerRecognition.Role ?? fileNameRole;
            return new IsoFieldSourceFile(
                filePath,
                role,
                frame.PixelWidth,
                frame.PixelHeight,
                RoleDetection: roleDetection);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return new IsoFieldSourceFile(
                filePath,
                fileNameRole,
                null,
                null,
                "не удалось прочитать изображение",
                ResolveRoleDetection(fileNameRole, IsoFieldHeaderRoleRecognition.NotDetected));
        }
    }

    private static IsoFieldRoleDetection ResolveRoleDetection(
        IsoFieldLayerRole? fileNameRole,
        IsoFieldHeaderRoleRecognition headerRecognition)
    {
        IsoFieldRoleDetectionKind kind = (fileNameRole, headerRecognition.Role) switch
        {
            (null, null) => IsoFieldRoleDetectionKind.NotDetected,
            (not null, null) => IsoFieldRoleDetectionKind.FileName,
            (null, not null) => IsoFieldRoleDetectionKind.Header,
            (not null, not null) when fileNameRole == headerRecognition.Role =>
                IsoFieldRoleDetectionKind.FileNameAndHeader,
            _ => IsoFieldRoleDetectionKind.Conflict
        };
        return new IsoFieldRoleDetection(
            kind,
            fileNameRole,
            headerRecognition.Role,
            headerRecognition.Confidence > 0 ? headerRecognition.Confidence : null);
    }
}
