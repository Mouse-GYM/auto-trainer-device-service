using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Endpoints;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

public class PageRequestTests
{
    [Fact]
    public void From_Defaults_WhenNull()
    {
        var pr = PageRequest.From(null, null);
        Assert.Equal(1, pr.Page);
        Assert.Equal(PageRequest.DefaultPageSize, pr.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void From_ClampsPage(int input, int expected)
    {
        Assert.Equal(expected, PageRequest.From(input, null).Page);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(50, 50)]
    [InlineData(5000, PageRequest.MaxPageSize)]
    public void From_ClampsPageSize(int input, int expected)
    {
        Assert.Equal(expected, PageRequest.From(1, input).PageSize);
    }

    [Fact]
    public void Skip_IsZeroBasedOffset()
    {
        Assert.Equal(0, new PageRequest(1, 50).Skip);
        Assert.Equal(50, new PageRequest(2, 50).Skip);
        Assert.Equal(40, new PageRequest(3, 20).Skip);
    }

    [Theory]
    [InlineData(0, 50, 0)]
    [InlineData(101, 50, 3)]
    [InlineData(100, 50, 2)]
    [InlineData(1, 50, 1)]
    public void PagedResult_TotalPages(int total, int pageSize, int expected)
    {
        var pr = new PagedResult<int>([], 1, pageSize, total);
        Assert.Equal(expected, pr.TotalPages);
    }

    [Fact]
    public void PagedResult_Empty_EchoesPageAndSize()
    {
        var pr = PagedResult<string>.Empty(2, 25);
        Assert.Empty(pr.Items);
        Assert.Equal(2, pr.Page);
        Assert.Equal(25, pr.PageSize);
        Assert.Equal(0, pr.TotalCount);
    }
}
