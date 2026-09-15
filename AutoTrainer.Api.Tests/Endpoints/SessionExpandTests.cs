using AutoTrainer.Api.Endpoints;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

public class SessionExpandTests
{
    [Fact]
    public void Empty_AllFalse()
    {
        Assert.True(SessionExpand.TryParse(null, out var e, out _));
        Assert.False(e.Trials);
        Assert.False(e.Batches);
    }

    [Fact]
    public void Trials()
    {
        Assert.True(SessionExpand.TryParse("trials", out var e, out _));
        Assert.True(e.Trials);
        Assert.False(e.Batches);
    }

    [Fact]
    public void TrialsRightHandReaches_ImpliesTrials()
    {
        Assert.True(SessionExpand.TryParse("trials.rightHandReaches", out var e, out _));
        Assert.True(e.Trials);
        Assert.True(e.TrialsRightHandReaches);
    }

    [Fact]
    public void Combo()
    {
        Assert.True(SessionExpand.TryParse("trials, batches", out var e, out _));
        Assert.True(e.Trials);
        Assert.True(e.Batches);
    }

    [Fact]
    public void UnknownToken_Fails()
    {
        Assert.False(SessionExpand.TryParse("trials,bogus", out _, out var error));
        Assert.Contains("bogus", error);
    }
}
