using AutoTrainer.Api.Endpoints;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

public class TimeWindowTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 15, 30, 45, DateTimeKind.Utc);

    private static TimeWindow Parse(string? value)
    {
        Assert.True(TimeWindow.TryParse(value, out var window));
        return window;
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("10m", 10 * 60)]
    [InlineData("2h", 2 * 3600)]
    [InlineData("5d", 5 * 86400)]
    [InlineData("3w", 3 * 7 * 86400)]
    public void Rolling_SubtractsFixedDurationFromNow(string token, long seconds)
    {
        Assert.Equal(Now - TimeSpan.FromSeconds(seconds), Parse(token).StartUtc(Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Default_IsRollingFiveDays(string? value)
    {
        Assert.Equal(Now - TimeSpan.FromDays(5), Parse(value).StartUtc(Now));
    }

    [Fact]
    public void Units_AreCaseInsensitive()
    {
        Assert.Equal(Parse("2h").StartUtc(Now), Parse("2H").StartUtc(Now));
        Assert.Equal(Parse("5c").StartUtc(Now), Parse("5C").StartUtc(Now));
    }

    // 1c == today: local midnight of the current local day, expressed in UTC.
    [Fact]
    public void CalendarOneDay_IsLocalMidnightToday()
    {
        var start = Parse("1c").StartUtc(Now);

        Assert.Equal(Now.ToLocalTime().Date.ToUniversalTime(), start);
        Assert.Equal(TimeSpan.Zero, start.ToLocalTime().TimeOfDay);        // exactly local midnight
        Assert.Equal(Now.ToLocalTime().Date, start.ToLocalTime().Date);   // today
        Assert.True(start <= Now);
    }

    // 5c == today plus the previous four calendar days: back to local midnight four days ago.
    [Fact]
    public void CalendarFiveDays_StepsBackFourLocalMidnights()
    {
        var start = Parse("5c").StartUtc(Now);

        Assert.Equal(TimeSpan.Zero, start.ToLocalTime().TimeOfDay);
        // Compare in local space so a DST shift between the two midnights doesn't matter.
        Assert.Equal(Parse("1c").StartUtc(Now).ToLocalTime().AddDays(-4), start.ToLocalTime());
    }

    [Theory]
    [InlineData("5x")]      // unknown unit
    [InlineData("abc")]     // no digits
    [InlineData("c")]       // unit only, no amount
    [InlineData("0c")]      // non-positive amount
    [InlineData("0d")]
    [InlineData("5cc")]     // more than one unit character
    [InlineData("-3d")]     // no leading digit
    [InlineData("5")]       // no unit
    public void Malformed_ReturnsFalse(string value)
    {
        Assert.False(TimeWindow.TryParse(value, out _));
    }
}
