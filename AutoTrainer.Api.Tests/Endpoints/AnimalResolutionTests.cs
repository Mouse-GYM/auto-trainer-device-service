using AutoTrainer.Api.Data;
using AutoTrainer.Api.Endpoints;
using AutoTrainer.Api.Options;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

public class AnimalResolutionTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteStorage _storage;

    public AnimalResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "atresolve-" + Guid.NewGuid().ToString("N"));
        _storage = new SqliteStorage(
            Microsoft.Extensions.Options.Options.Create(new DataOptions { SQLLiteLocation = _root }),
            NullLogger<SqliteStorage>.Instance);
        _storage.EnsureDirectories(out _);
    }

    private void TouchDatabase(string identifier) =>
        File.WriteAllText(_storage.GetAnimalDatabasePath(identifier), "");

    [Theory]
    [InlineData("../escape")]
    [InlineData("")]
    public void ProvidedInvalid_Is400(string identifier)
    {
        var res = AnimalResolution.Resolve(identifier, selectedIdentifier: null, _storage);
        // "" is treated as omitted (whitespace) -> Empty; "../escape" is provided+invalid -> 400.
        if (string.IsNullOrWhiteSpace(identifier))
            Assert.True(res.Empty);
        else
            Assert.IsType<BadRequest<string>>(res.Error);
    }

    [Fact]
    public void ProvidedButMissing_Is404()
    {
        var res = AnimalResolution.Resolve("mouse-1", selectedIdentifier: null, _storage);
        Assert.IsType<NotFound<string>>(res.Error);
    }

    [Fact]
    public void ProvidedAndExists_ResolvesToIdentifier()
    {
        TouchDatabase("mouse-1");
        var res = AnimalResolution.Resolve("mouse-1", selectedIdentifier: null, _storage);
        Assert.Null(res.Error);
        Assert.False(res.Empty);
        Assert.Equal("mouse-1", res.Identifier);
    }

    [Fact]
    public void Omitted_UsesSelected_WithoutFileCheck()
    {
        // No file for "selected" — the omitted/selected branch does NOT 404.
        var res = AnimalResolution.Resolve(null, selectedIdentifier: "selected", _storage);
        Assert.Null(res.Error);
        Assert.False(res.Empty);
        Assert.Equal("selected", res.Identifier);
    }

    [Fact]
    public void Omitted_NoneSelected_IsEmpty()
    {
        var res = AnimalResolution.Resolve(null, selectedIdentifier: null, _storage);
        Assert.True(res.Empty);
        Assert.Null(res.Error);
        Assert.Null(res.Identifier);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }
}
