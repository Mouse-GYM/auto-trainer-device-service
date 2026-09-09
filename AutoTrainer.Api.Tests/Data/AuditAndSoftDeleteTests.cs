using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoTrainer.Api.Tests.Data;

public class AuditAndSoftDeleteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DeviceDbContext> _options;

    public AuditAndSoftDeleteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DeviceDbContext>().UseSqlite(_connection).Options;
        using var db = new DeviceDbContext(_options);
        db.Database.Migrate();
    }

    private DeviceDbContext NewContext() => new(_options);

    [Fact]
    public void Add_SetsCreatedAndUpdated()
    {
        using var db = NewContext();
        var row = new SystemConfiguration { DeviceId = "d1" };
        db.SystemConfigurations.Add(row);
        db.SaveChanges();

        Assert.NotEqual(default, row.CreatedAt);
        Assert.Equal(row.CreatedAt, row.UpdatedAt);
    }

    [Fact]
    public void Modify_BumpsUpdatedOnly()
    {
        using var db = NewContext();
        var row = new SystemConfiguration { DeviceId = "d1" };
        db.SystemConfigurations.Add(row);
        db.SaveChanges();
        var created = row.CreatedAt;

        row.DeviceId = "d2";
        db.SaveChanges();

        Assert.Equal(created, row.CreatedAt);
        Assert.True(row.UpdatedAt >= created);
    }

    [Fact]
    public void Remove_SoftDeletes_AndIsFilteredOut()
    {
        using (var db = NewContext())
        {
            db.SystemConfigurations.Add(new SystemConfiguration { DeviceId = "d1" });
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            var row = db.SystemConfigurations.Single();
            db.SystemConfigurations.Remove(row);
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            Assert.Empty(db.SystemConfigurations.ToList());
            var all = db.SystemConfigurations.IgnoreQueryFilters().ToList();
            Assert.Single(all);
            Assert.NotNull(all[0].DeletedAt);
        }
    }

    // A deleted note is what the note log's DELETE writes, so this pins the behaviour the store relies on:
    // Remove leaves the row in place with DeletedAt stamped, and every unfiltered read stops seeing it.
    [Fact]
    public void SystemNote_Remove_SoftDeletes_AndIsFilteredOut()
    {
        DateTime created;

        using (var db = NewContext())
        {
            var row = new SystemNote { Body = "bench 3, left rack" };
            db.SystemNotes.Add(row);
            db.SaveChanges();
            created = row.CreatedAt;
        }

        using (var db = NewContext())
        {
            db.SystemNotes.Remove(db.SystemNotes.Single());
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            Assert.Empty(db.SystemNotes.ToList());

            var row = Assert.Single(db.SystemNotes.IgnoreQueryFilters().ToList());
            Assert.NotNull(row.DeletedAt);
            Assert.True(row.UpdatedAt >= created);
            Assert.Equal("bench 3, left rack", row.Body);
        }
    }

    public void Dispose() => _connection.Dispose();
}
