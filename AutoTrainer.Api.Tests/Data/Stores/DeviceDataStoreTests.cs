using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Data.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoTrainer.Api.Tests.Data.Stores;

public class DeviceDataStoreTests
{
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
