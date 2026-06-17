using AutoTrainer.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AutoTrainer.Api.Tests;

// A keepalive in-memory SQLite IDbContextFactory<DeviceDbContext> for store tests.
// The single connection is kept open for the lifetime of the factory so the
// in-memory database survives between created contexts.
public sealed class TestDeviceDbContextFactory : IDbContextFactory<DeviceDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DeviceDbContext> _options;

    public TestDeviceDbContextFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DeviceDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public DeviceDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
