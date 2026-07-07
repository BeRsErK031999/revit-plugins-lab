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
}
