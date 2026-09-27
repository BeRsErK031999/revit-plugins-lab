using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishLegacyRoomRowResolverTests
{
    [Fact]
    public void Resolve_ReturnsEveryRealRoomInGroupedRow()
    {
        Dictionary<long, string> rooms = new() { [11] = "101", [12] = "102", [13] = "103" };
        Assert.Equal(new long[] { 13, 11 }, FinishLegacyRoomRowResolver.Resolve("103, 101", rooms));
    }

    [Theory]
    [InlineData("")]
    [InlineData("101, ")]
    [InlineData("101, 101")]
    [InlineData("999")]
    [InlineData("Кабинет")]
    public void Resolve_RejectsMissingOrAmbiguousArchiveIdentifiers(string roomList)
    {
        Assert.Throws<InvalidOperationException>(() => FinishLegacyRoomRowResolver.Resolve(
            roomList, new Dictionary<long, string> { [11] = "101" }));
    }

    [Fact]
    public void Resolve_DoesNotChooseArbitraryRoomWhenNumbersRepeat()
    {
        Dictionary<long, string> rooms = new() { [11] = "101", [12] = "101" };
        Assert.Throws<InvalidOperationException>(() => FinishLegacyRoomRowResolver.Resolve("101", rooms));
    }
}
