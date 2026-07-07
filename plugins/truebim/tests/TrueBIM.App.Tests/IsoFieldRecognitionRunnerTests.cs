using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRecognitionRunnerTests
{
    [Fact]
    public void StubRecognitionRunner_ReturnsEmptyResult()
    {
        StubIsoFieldRecognitionRunner runner = new();

        var result = runner.Run(sourcePath: null);

        Assert.Empty(result.Polylines);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void IsoFieldFilePicker_ImplementsPickerContract()
    {
        Assert.IsAssignableFrom<IIsoFieldFilePicker>(new IsoFieldFilePicker());
    }

    [Fact]
    public void IsoFieldJsonReader_ReadsValidRecognitionResult()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "schemaVersion": "1.0",
              "polylines": [
                {
                  "id": "zone-a",
                  "zoneName": "Zone A",
                  "confidence": 0.9,
                  "points": [
                    { "x": 10.0, "y": 20.0 },
                    { "x": 30.0, "y": 40.0 }
                  ]
                }
              ],
              "diagnostics": [
                "sample"
              ]
            }
            """);

        try
        {
            var result = new IsoFieldJsonReader().Read(filePath);

            var polyline = Assert.Single(result.Polylines);
            Assert.Equal("zone-a", polyline.Id);
            Assert.Equal("Zone A", polyline.ZoneName);
            Assert.Equal(0.9, polyline.Confidence);
            Assert.Equal(2, polyline.Points.Count);
            Assert.Equal(10.0, polyline.Points[0].X);
            Assert.Equal(20.0, polyline.Points[0].Y);
            Assert.Equal("sample", Assert.Single(result.Diagnostics));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void IsoFieldJsonReader_RejectsPolylineWithoutEnoughPoints()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "schemaVersion": "1.0",
              "polylines": [
                {
                  "id": "zone-a",
                  "points": [
                    { "x": 10.0, "y": 20.0 }
                  ]
                }
              ]
            }
            """);

        try
        {
            Assert.Throws<InvalidDataException>(() => new IsoFieldJsonReader().Read(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
