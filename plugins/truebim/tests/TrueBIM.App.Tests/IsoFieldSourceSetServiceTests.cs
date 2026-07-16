using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldSourceSetServiceTests
{
    [Fact]
    public void Build_DetectsFourRequiredRolesAndImageSize()
    {
        string directory = CreateTempDirectory();
        try
        {
            string[] paths = CreateSourceImages(directory, 32, 18);

            IsoFieldSourceSet sourceSet = new IsoFieldSourceSetService().Build(paths);

            Assert.True(sourceSet.IsComplete);
            Assert.True(sourceSet.HasConsistentImageSize);
            Assert.Empty(sourceSet.ValidationMessages);
            Assert.Equal(IsoFieldLayerRole.As1X, sourceSet.Files[0].Role);
            Assert.Equal(32, sourceSet.Files[0].PixelWidth);
            Assert.Equal(18, sourceSet.Files[0].PixelHeight);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Build_ReportsMissingDuplicateAndMismatchedImages()
    {
        string directory = CreateTempDirectory();
        try
        {
            string as1X = CreatePng(directory, "result_As1X1.png", 32, 18);
            string duplicateAs1X = CreatePng(directory, "copy_As1X.png", 32, 18);
            string as3Y = CreatePng(directory, "result_As3Y1.png", 40, 18);
            string as4Y = CreatePng(directory, "result_As4Y1.png", 32, 18);

            IsoFieldSourceSet sourceSet = new IsoFieldSourceSetService().Build(
                [as1X, duplicateAs1X, as3Y, as4Y]);

            Assert.False(sourceSet.IsComplete);
            Assert.Contains(IsoFieldLayerRole.As2X, sourceSet.MissingRoles);
            Assert.Contains(IsoFieldLayerRole.As1X, sourceSet.DuplicateRoles);
            Assert.Contains(sourceSet.ValidationMessages, message => message.Contains("Размеры изображений различаются", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AssignRole_CompletesFileWhoseRoleWasNotDetected()
    {
        string directory = CreateTempDirectory();
        try
        {
            string[] paths = CreateSourceImages(directory, 32, 18);
            string unknownPath = Path.Combine(directory, "result_As4Y1.png");
            string renamedPath = Path.Combine(directory, "fourth-layer.png");
            File.Move(unknownPath, renamedPath);
            paths[3] = renamedPath;
            IsoFieldSourceSetService service = new();
            IsoFieldSourceSet sourceSet = service.Build(paths);

            IsoFieldSourceSet assigned = service.AssignRole(
                sourceSet,
                renamedPath,
                IsoFieldLayerRole.As4Y);

            Assert.True(assigned.IsComplete);
            Assert.Equal(IsoFieldLayerRole.As4Y, assigned.Files[3].Role);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecognitionService_MergesLayersAndKeepsRoleInPolyline()
    {
        IsoFieldSourceFile[] files = IsoFieldSourceSet.RequiredRoles
            .Select(role => new IsoFieldSourceFile($"C:\\maps\\{role}.png", role, 32, 18))
            .ToArray();
        IsoFieldSourceSet sourceSet = new(files);

        IsoFieldRecognitionResult result = new IsoFieldSourceSetRecognitionService().Run(
            sourceSet,
            new FakeRecognitionRunner());

        Assert.Equal(4, result.Polylines.Count);
        Assert.Equal(4, result.Diagnostics.Count);
        Assert.Collection(
            result.Polylines,
            polyline => Assert.Equal(IsoFieldLayerRole.As1X, polyline.LayerRole),
            polyline => Assert.Equal(IsoFieldLayerRole.As2X, polyline.LayerRole),
            polyline => Assert.Equal(IsoFieldLayerRole.As3Y, polyline.LayerRole),
            polyline => Assert.Equal(IsoFieldLayerRole.As4Y, polyline.LayerRole));
        Assert.All(result.Polylines, polyline => Assert.StartsWith(polyline.LayerRole + ":", polyline.Id, StringComparison.Ordinal));
        Assert.Equal("[As1X] fake diagnostic", result.Diagnostics[0]);
    }

    private static string[] CreateSourceImages(string directory, int width, int height)
    {
        return
        [
            CreatePng(directory, "result_As1X1.png", width, height),
            CreatePng(directory, "result_As2X1.png", width, height),
            CreatePng(directory, "result_As3Y1.png", width, height),
            CreatePng(directory, "result_As4Y1.png", width, height)
        ];
    }

    private static string CreatePng(string directory, string fileName, int width, int height)
    {
        string path = Path.Combine(directory, fileName);
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
        return path;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TrueBIM-IsoFieldSet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeRecognitionRunner : IIsoFieldRecognitionRunner
    {
        public IsoFieldRecognitionResult Run(string? sourcePath)
        {
            string id = Path.GetFileNameWithoutExtension(sourcePath ?? "source");
            return new IsoFieldRecognitionResult(
                [
                    new IsoFieldPolyline(
                        id,
                        [new IsoFieldPoint(0, 0), new IsoFieldPoint(10, 10)])
                ],
                ["fake diagnostic"]);
        }
    }
}
