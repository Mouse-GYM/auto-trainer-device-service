using AutoTrainer.Api.Data;
using Xunit;

namespace AutoTrainer.Api.Tests.Data;

public class DayPathTests
{
    [Fact]
    public void TryGetDay_ParsesFinalPathComponent()
    {
        Assert.Equal(new DateOnly(2026, 7, 13), DayPath.TryGetDay("/data/mouse-1/20260713"));
    }

    [Fact]
    public void TryGetDay_IgnoresTrailingSeparator()
    {
        Assert.Equal(new DateOnly(2026, 7, 13), DayPath.TryGetDay("/data/mouse-1/20260713/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/data/mouse-1/not-a-date")]
    [InlineData("/data/mouse-1/2026071")]
    public void TryGetDay_ReturnsNullWhenUnparseable(string? dayPath)
    {
        Assert.Null(DayPath.TryGetDay(dayPath));
    }
}
