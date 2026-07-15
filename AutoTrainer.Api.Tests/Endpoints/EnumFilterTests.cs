using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Endpoints;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

public class EnumFilterTests
{
    [Fact]
    public void Null_IsEmptyAndOk()
    {
        Assert.True(EnumFilter.TryParse<ApiAlarmKind>(null, out var values, out var error));
        Assert.Empty(values);
        Assert.Null(error);
    }

    [Fact]
    public void ParsesNames()
    {
        Assert.True(EnumFilter.TryParse<ApiAlarmKind>(["AnimalMissing", "Thrashing"], out var values, out _));
        Assert.Equal([ApiAlarmKind.AnimalMissing, ApiAlarmKind.Thrashing], values);
    }

    [Fact]
    public void ParsesNumericCodes()
    {
        Assert.True(EnumFilter.TryParse<ApiAlarmKind>(["201"], out var values, out _));
        Assert.Equal(ApiAlarmKind.AnimalMissing, Assert.Single(values));
    }

    [Fact]
    public void ParsesCommaList()
    {
        Assert.True(EnumFilter.TryParse<ApiAlarmKind>(["201,301"], out var values, out _));
        Assert.Equal([ApiAlarmKind.AnimalMissing, ApiAlarmKind.Thrashing], values);
    }

    [Fact]
    public void ParsesRepeatedValues()
    {
        Assert.True(EnumFilter.TryParse<ApiDetectorKind>(["FrontDoor", "102"], out var values, out _));
        Assert.Equal([ApiDetectorKind.FrontDoor, ApiDetectorKind.SlidingDoor], values);
    }

    [Fact]
    public void UnknownName_Fails()
    {
        Assert.False(EnumFilter.TryParse<ApiAlarmKind>(["NotAThing"], out _, out var error));
        Assert.Contains("NotAThing", error);
    }

    [Fact]
    public void OutOfRangeNumber_Fails()
    {
        Assert.False(EnumFilter.TryParse<ApiAlarmKind>(["999"], out _, out var error));
        Assert.Contains("999", error);
    }
}
