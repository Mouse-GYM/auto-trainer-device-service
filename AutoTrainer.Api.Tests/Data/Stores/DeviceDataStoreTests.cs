using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Entities = AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Tests.Data.Stores;

public class DeviceDataStoreTests
{
    private static readonly PageRequest FirstPage = PageRequest.From(null, null);

    private static (TestDeviceDbContextFactory factory, DeviceDataStore store) NewStore()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        return (factory, store);
    }

    // Adds an AlarmHistory row and backdates its CreatedAt (the Modified audit only bumps UpdatedAt, so the
    // backdated CreatedAt survives), giving tests control over the observation time.
    private static async Task SeedAlarmAsync(TestDeviceDbContextFactory factory, ApiAlarmKind kind,
        DateTime createdAt, bool isEnabled = true)
    {
        using var db = factory.CreateDbContext();
        var e = new Entities.AlarmHistory { AlarmId = kind, IsActive = true, IsEnabled = isEnabled };
        db.AlarmHistory.Add(e);
        await db.SaveChangesAsync();
        e.CreatedAt = createdAt;
        await db.SaveChangesAsync();
    }

    private static async Task SeedDetectorAsync(TestDeviceDbContextFactory factory, ApiDetectorKind kind,
        DateTime createdAt, bool isEnabled = true)
    {
        using var db = factory.CreateDbContext();
        var e = new Entities.DetectorHistory { DetectorId = kind, IsActive = true, IsEnabled = isEnabled };
        db.DetectorHistory.Add(e);
        await db.SaveChangesAsync();
        e.CreatedAt = createdAt;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAlarms_FiltersByKind_Pages_AndNewestFirst()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            await SeedAlarmAsync(factory, ApiAlarmKind.ExternalDoors, now.AddMinutes(-3));
            await SeedAlarmAsync(factory, ApiAlarmKind.AnimalMissing, now.AddMinutes(-2));
            await SeedAlarmAsync(factory, ApiAlarmKind.AnimalMissing, now.AddMinutes(-1));

            // No filter: total counts all three; ObservedAt is the row's CreatedAt; newest first.
            var all = await store.GetAlarmsAsync(now.AddDays(-1), [], null, FirstPage);
            Assert.Equal(3, all.TotalCount);
            Assert.Equal(now.AddMinutes(-1), all.Items[0].ObservedAt, TimeSpan.FromSeconds(1));

            // Kind filter counts only the filtered set.
            var missing = await store.GetAlarmsAsync(now.AddDays(-1), [ApiAlarmKind.AnimalMissing], null, FirstPage);
            Assert.Equal(2, missing.TotalCount);
            Assert.All(missing.Items, a => Assert.Equal(ApiAlarmKind.AnimalMissing, a.AlarmId));

            // Paging: pageSize 2 returns a page of 2 but total is still 3.
            var paged = await store.GetAlarmsAsync(now.AddDays(-1), [], null, new PageRequest(1, 2));
            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(3, paged.TotalCount);
        }
    }

    [Fact]
    public async Task GetAlarms_TimeWindow_ExcludesOld()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            await SeedAlarmAsync(factory, ApiAlarmKind.ExternalDoors, now.AddMinutes(-1));
            await SeedAlarmAsync(factory, ApiAlarmKind.ExternalDoors, now.AddDays(-10));

            var recent = await store.GetAlarmsAsync(now.AddDays(-5), [], null, FirstPage);
            Assert.Equal(1, recent.TotalCount);
        }
    }

    // Null means "either" -- the filter is opt-in, so the unfiltered query still returns both states.
    [Fact]
    public async Task GetAlarms_FiltersByIsEnabled()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            await SeedAlarmAsync(factory, ApiAlarmKind.ExternalDoors, now.AddMinutes(-2), isEnabled: true);
            await SeedAlarmAsync(factory, ApiAlarmKind.ExternalDoors, now.AddMinutes(-1), isEnabled: false);

            var enabled = await store.GetAlarmsAsync(now.AddDays(-1), [], true, FirstPage);
            Assert.Equal(1, enabled.TotalCount);
            Assert.True(enabled.Items[0].IsEnabled);

            var disabled = await store.GetAlarmsAsync(now.AddDays(-1), [], false, FirstPage);
            Assert.Equal(1, disabled.TotalCount);
            Assert.False(disabled.Items[0].IsEnabled);

            var either = await store.GetAlarmsAsync(now.AddDays(-1), [], null, FirstPage);
            Assert.Equal(2, either.TotalCount);
        }
    }

    // The filter must narrow the total too, not just the returned page.
    [Fact]
    public async Task GetAlarms_IsEnabledCombinesWithKindFilter()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            await SeedAlarmAsync(factory, ApiAlarmKind.ExternalDoors, now.AddMinutes(-3), isEnabled: true);
            await SeedAlarmAsync(factory, ApiAlarmKind.ExternalDoors, now.AddMinutes(-2), isEnabled: false);
            await SeedAlarmAsync(factory, ApiAlarmKind.AnimalMissing, now.AddMinutes(-1), isEnabled: true);

            var result = await store.GetAlarmsAsync(now.AddDays(-1), [ApiAlarmKind.ExternalDoors], true, FirstPage);

            Assert.Equal(1, result.TotalCount);
            Assert.Equal(ApiAlarmKind.ExternalDoors, result.Items[0].AlarmId);
            Assert.True(result.Items[0].IsEnabled);
        }
    }

    [Fact]
    public async Task GetDetectors_FiltersByIsEnabled()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            await SeedDetectorAsync(factory, ApiDetectorKind.FrontDoor, now.AddMinutes(-2), isEnabled: true);
            await SeedDetectorAsync(factory, ApiDetectorKind.FrontDoor, now.AddMinutes(-1), isEnabled: false);

            var enabled = await store.GetDetectorsAsync(now.AddDays(-1), [], true, FirstPage);
            Assert.Equal(1, enabled.TotalCount);
            Assert.True(enabled.Items[0].IsEnabled);

            var disabled = await store.GetDetectorsAsync(now.AddDays(-1), [], false, FirstPage);
            Assert.Equal(1, disabled.TotalCount);
            Assert.False(disabled.Items[0].IsEnabled);

            var either = await store.GetDetectorsAsync(now.AddDays(-1), [], null, FirstPage);
            Assert.Equal(2, either.TotalCount);
        }
    }

    [Fact]
    public async Task GetDetectors_FiltersAndProjects()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            await SeedDetectorAsync(factory, ApiDetectorKind.FrontDoor, now.AddMinutes(-2));
            await SeedDetectorAsync(factory, ApiDetectorKind.SlidingDoor, now.AddMinutes(-1));

            var front = await store.GetDetectorsAsync(now.AddDays(-1), [ApiDetectorKind.FrontDoor], null, FirstPage);
            var row = Assert.Single(front.Items);
            Assert.Equal(ApiDetectorKind.FrontDoor, row.DetectorId);
            Assert.True(row.IsActive);
        }
    }

    [Fact]
    public async Task GetAnimals_ListsRegistry_OrderedByName()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-b", "Beta");
            await store.SetAnimalNameAsync("id-a", "Alpha");

            var animals = await store.GetAnimalsAsync();

            Assert.Equal(2, animals.Count);
            Assert.Equal("Alpha", animals[0].Name);
            Assert.Equal("id-a", animals[0].Identifier);
            Assert.NotEqual(0, animals[0].Id);
            Assert.NotEqual(default, animals[0].FirstSeen);
            Assert.NotEqual(default, animals[0].LastUpdated);
        }
    }

    [Fact]
    public async Task Initialize_RunsTheMigration_AndLeavesTheStoreUsable()
    {
        using var factory = new TestDeviceDbContextFactory();
        var store = new DeviceDataStore(factory, NullLogger<DeviceDataStore>.Instance);

        await store.InitializeAsync();
        await store.InitializeAsync();   // idempotent: migrating an already-migrated database is a no-op

        await store.SetAnimalNameAsync("id-a", "Alpha");
        Assert.Single(await store.GetAnimalsAsync());
    }

    [Fact]
    public async Task AddSystemConfiguration_MapsAllFields()
    {
        using var factory = new TestDeviceDbContextFactory();
        var store = new DeviceDataStore(factory, NullLogger<DeviceDataStore>.Instance);

        await store.InitializeAsync();

        var config = new ApiSystemConfiguration
        {
            ApplicationVersion = "1.0.0",
            DeviceId = "device-7",
            ConfigurationLocation = "/cfg",
            DataLocation = "/data",
            AnimalLocation = "/animals",
            LogLocation = "/logs",
            InferenceModel = "model-x"
        };

        await store.AddSystemConfigurationAsync(config);

        using var db = factory.CreateDbContext();
        var row = Assert.Single(db.SystemConfigurations.ToList());
        Assert.Equal("1.0.0", row.ApplicationVersion);
        Assert.Equal("device-7", row.DeviceId);
        Assert.Equal("/cfg", row.ConfigurationLocation);
        Assert.Equal("/data", row.DataLocation);
        Assert.Equal("/animals", row.AnimalLocation);
        Assert.Equal("/logs", row.LogLocation);
        Assert.Equal("model-x", row.InferenceModel);
    }
}
