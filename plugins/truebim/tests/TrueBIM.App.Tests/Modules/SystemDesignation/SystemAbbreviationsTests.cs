using TrueBIM.App.Modules.SystemDesignation;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SystemDesignation;

public sealed class SystemAbbreviationsTests
{
    [Theory]
    [InlineData("П1,В1", "В1, П1")]
    [InlineData(" В1,В1, В1 ", "В1")]
    [InlineData(",,  ", "")]
    [InlineData("Т1.1,Т1,Т1.1", "Т1, Т1.1")]
    [InlineData("П 1/2,П 1/2", "П 1/2")]
    public void FormatsDistinctAbbreviations(string input, string expected)
    {
        Assert.Equal(expected, SystemAbbreviations.Format(input.Split(',')));
    }
}
