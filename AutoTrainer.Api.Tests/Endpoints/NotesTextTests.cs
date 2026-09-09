using AutoTrainer.Api.Endpoints;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

public class NotesTextTests
{
    [Fact]
    public void Null_IsRejected_AndNamesTheField()
    {
        Assert.False(NotesText.TryAccept(null, out var body, out var error));
        Assert.Equal("", body);
        Assert.Contains("body", error);
    }

    // Deleting the note is how a note is removed, so an empty body is a mistake rather than a way to clear one.
    [Fact]
    public void EmptyString_IsRejected()
    {
        Assert.False(NotesText.TryAccept("", out var body, out var error));
        Assert.Equal("", body);
        Assert.Contains("body", error);
    }

    [Fact]
    public void WhitespaceOnly_IsRejected()
    {
        Assert.False(NotesText.TryAccept("   \t\n ", out var body, out var error));
        Assert.Equal("", body);
        Assert.Contains("body", error);
    }

    // Operators paste formatted text and leading indentation is theirs to keep.
    [Fact]
    public void AcceptedValue_IsReturnedUntrimmed()
    {
        Assert.True(NotesText.TryAccept("  skittish after 16:00  ", out var body, out _));
        Assert.Equal("  skittish after 16:00  ", body);
    }

    [Fact]
    public void ValueAtTheLimit_IsAccepted()
    {
        var value = new string('x', NotesText.MaxLength);

        Assert.True(NotesText.TryAccept(value, out var body, out var error));
        Assert.Equal(value, body);
        Assert.Null(error);
    }

    [Fact]
    public void ValueOverTheLimit_IsRejected()
    {
        var value = new string('x', NotesText.MaxLength + 1);

        Assert.False(NotesText.TryAccept(value, out var body, out var error));
        Assert.Equal("", body);
        Assert.Contains(NotesText.MaxLength.ToString(), error);
    }
}
