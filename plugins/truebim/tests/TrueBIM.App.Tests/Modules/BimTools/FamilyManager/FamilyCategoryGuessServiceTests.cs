using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyCategoryGuessServiceTests
{
    private readonly FamilyCategoryGuessService service = new();

    [Theory]
    [InlineData(@"C:\Library\Doors\Single Door.rfa", "Двери")]
    [InlineData(@"C:\Library\Окна\Окно_1200.rfa", "Окна")]
    [InlineData(@"C:\Library\TitleBlocks\Штамп А1.rfa", "Штампы")]
    [InlineData(@"C:\Library\Mechanical\duct diffuser.rfa", "ОВиК")]
    public void Guess_UsesPathAndFileNameTokens(string filePath, string expectedCategory)
    {
        Assert.Equal(expectedCategory, service.Guess(filePath));
    }

    [Fact]
    public void Guess_ReturnsUnknownForUnmatchedPath()
    {
        Assert.Equal(FamilyManagerDefaults.UnknownCategory, service.Guess(@"C:\Library\Random\Item.rfa"));
    }
}
