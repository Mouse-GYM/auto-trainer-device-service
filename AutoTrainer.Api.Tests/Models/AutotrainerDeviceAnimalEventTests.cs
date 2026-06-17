using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AutoTrainer.Api.Tests.Models;

public class AutotrainerDeviceAnimalEventTests
{
    private static (AutotrainerDevice device, Mock<ICommandTaskQueue> queue, Mock<IMessageHub> clients,
        Mock<IDeviceDataStore> deviceStore, Mock<IAnimalDataStore> animalStore) Build()
    {
        var queue = new Mock<ICommandTaskQueue>();
        var clients = new Mock<IMessageHub>();
        var hub = new Mock<IHubContext<MessageHub, IMessageHub>>();
        var hubClients = new Mock<IHubClients<IMessageHub>>();
        hubClients.Setup(c => c.All).Returns(clients.Object);
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        var deviceStore = new Mock<IDeviceDataStore>();
        var animalStore = new Mock<IAnimalDataStore>();

        var device = new AutotrainerDevice(
            queue.Object, hub.Object, NullLogger<AutotrainerDevice>.Instance,
            deviceStore.Object, animalStore.Object);

        return (device, queue, clients, deviceStore, animalStore);
    }

    private static async Task DrainAsync(AutotrainerDevice device)
    {
        // DeviceUpdateWorker isn't running in tests; execute the queued action(s) ourselves.
        while (device.UpdateReader.TryRead(out var action))
        {
            await action();
        }
    }

    private static ApiEvent AnimalEvent(ApiEventKind kind, ApiAnimalStatus status) => new()
    {
        Kind = kind,
        Context = JsonSerializer.SerializeToElement(status, JsonDefaults.CamelCase)
    };

    [Fact]
    public async Task AnimalSelected_PersistsAnimalInfo()
    {
        var (device, _, _, _, animalStore) = Build();

        device.OnApiEvent(AnimalEvent(ApiEventKind.AnimalSelected,
            new ApiAnimalStatus { Identifier = "mouse-1", Name = "A" }));
        await DrainAsync(device);

        animalStore.Verify(s => s.AddAnimalInfoAsync(
            It.Is<ApiAnimalStatus>(a => a.Identifier == "mouse-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnimalSelected_EmptyIdentifier_DoesNotPersist()
    {
        var (device, _, _, _, animalStore) = Build();

        device.OnApiEvent(AnimalEvent(ApiEventKind.AnimalSelected,
            new ApiAnimalStatus { Identifier = "", Name = "A" }));
        await DrainAsync(device);

        animalStore.Verify(s => s.AddAnimalInfoAsync(
            It.IsAny<ApiAnimalStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnimalSelected_StoreThrows_StillBroadcasts()
    {
        var (device, _, clients, _, animalStore) = Build();
        animalStore.Setup(s => s.AddAnimalInfoAsync(It.IsAny<ApiAnimalStatus>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var evt = AnimalEvent(ApiEventKind.AnimalSelected, new ApiAnimalStatus { Identifier = "mouse-1" });
        device.OnApiEvent(evt);
        await DrainAsync(device);

        clients.Verify(c => c.EventReceived(It.IsAny<ApiEvent>()), Times.Once);
    }

    [Fact]
    public async Task GetConfiguration_StoreThrows_StillEnqueuesGetStatus()
    {
        var (device, queue, _, deviceStore, _) = Build();
        deviceStore.Setup(s => s.AddSystemConfigurationAsync(It.IsAny<ApiSystemConfiguration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var config = new ApiSystemConfiguration { DeviceId = "d1" };
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(
            JsonSerializer.Serialize(config, JsonDefaults.CamelCase), JsonDefaults.CamelCase);

        var response = new ApiCommandRequestResponse(
            nonce: 1, command: ApiCommandKind.GetConfiguration,
            result: ApiCommandRequestResult.Success, data: data);

        device.OnCommandResponse(response);
        await DrainAsync(device);

        queue.Verify(q => q.EnqueueAsync(
            It.Is<ApiCommandRequest>(r => r.Command == ApiCommandKind.GetStatus)), Times.Once);
    }
}
