using AutoTrainer.Api.Endpoints;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

public class SortRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    [InlineData("+")]
    public void From_BlankOrDirectionOnly_IsNoSort(string? input)
    {
        var sort = SortRequest.From(input);
        Assert.False(sort.HasSort);
        Assert.Null(sort.Field);
    }

    [Theory]
    [InlineData("identifier")]
    [InlineData("+identifier")]
    [InlineData("  identifier  ")]
    public void From_Ascending(string input)
    {
        var sort = SortRequest.From(input);
        Assert.True(sort.HasSort);
        Assert.False(sort.Descending);
        Assert.True(sort.Is("identifier"));
    }

    [Theory]
    [InlineData("-identifier")]
    [InlineData("- identifier")]
    public void From_Descending(string input)
    {
        var sort = SortRequest.From(input);
        Assert.True(sort.Descending);
        Assert.True(sort.Is("identifier"));
    }

    [Fact]
    public void Is_IsCaseInsensitive()
    {
        Assert.True(SortRequest.From("Identifier").Is("identifier"));
        Assert.True(SortRequest.From("IDENTIFIER").Is("identifier"));
    }

    [Fact]
    public void Is_FalseForNoSort()
    {
        Assert.False(default(SortRequest).Is("identifier"));
    }
}
