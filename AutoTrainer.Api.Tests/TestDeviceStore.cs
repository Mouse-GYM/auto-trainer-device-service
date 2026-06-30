using AutoTrainer.Api.Data.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoTrainer.Api.Tests;

// A real, migrated DeviceDataStore over a keepalive in-memory device database, for tests that need one --
// notably AnimalDataStore, which now registers/updates animals in the device database.
internal static class TestDeviceStore
{
    public static (TestDeviceDbContextFactory factory, DeviceDataStore store) CreateMigrated()
    {
        var factory = new TestDeviceDbContextFactory();

        using (var db = factory.CreateDbContext())
            db.Database.Migrate();

        var store = new DeviceDataStore(factory, NullLogger<DeviceDataStore>.Instance);
        return (factory, store);
    }
}
