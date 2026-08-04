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

public class AutotrainerDeviceEmergencyEventTests
{
    private static (AutotrainerDevice device, Mock<IMessageHub> clients, Mock<IDeviceDataStore> deviceStore) Build()
    {
        var clients = new Mock<IMessageHub>();
        var hub = new Mock<IHubContext<MessageHub, IMessageHub>>();
        var hubClients = new Mock<IHubClients<IMessageHub>>();
        hubClients.Setup(c => c.All).Returns(clients.Object);
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        var deviceStore = new Mock<IDeviceDataStore>();

        var device = new AutotrainerDevice(
            new Mock<ICommandTaskQueue>().Object, hub.Object, NullLogger<AutotrainerDevice>.Instance,
            deviceStore.Object, new Mock<IAnimalDataStore>().Object);

        return (device, clients, deviceStore);
    }

    // DeviceUpdateWorker isn't running in tests; execute the queued action(s) ourselves.
    private static async Task DrainAsync(AutotrainerDevice device)
    {
        while (device.UpdateReader.TryRead(out var action))
        {
            await action();
        }
    }

    private static ApiEvent Event(ApiEventKind kind, object? payload, double when = 1_770_000_000,
        ulong index = 999) => new()
        {
            Kind = kind,
            When = when,
            Index = index,
            Context = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonDefaults.CamelCase)
        };

    [Fact]
    public async Task EmergencyStop_PersistsPayloadAndIndex()
    {
        var (device, _, store) = Build();

        device.OnApiEvent(Event(ApiEventKind.EmergencyStop, new ApiEmergencyStopPayload
        {
            Reason = "alarm-monitor: DOORS_OPEN",
            ActiveAlarms = [ApiAlarmKind.ExternalDoors]
        }, index: 4242));
        await DrainAsync(device);

        store.Verify(s => s.AddEmergencyStopAsync(
            It.Is<ApiEmergencyStopPayload>(p =>
                p.Reason == "alarm-monitor: DOORS_OPEN" &&
                p.ActiveAlarms.Count == 1 &&
                p.ActiveAlarms[0] == ApiAlarmKind.ExternalDoors),
            It.IsAny<DateTime>(), 4242L, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmergencyResume_PersistsReason()
    {
        var (device, _, store) = Build();

        device.OnApiEvent(Event(ApiEventKind.EmergencyResume,
            new ApiReasonPayload { Reason = "alarm-monitor-resumed" }, index: 7));
        await DrainAsync(device);

        store.Verify(s => s.AddEmergencyResumeAsync(
            It.Is<ApiReasonPayload>(p => p.Reason == "alarm-monitor-resumed"),
            It.IsAny<DateTime>(), 7L, null, It.IsAny<CancellationToken>()), Times.Once);

        store.Verify(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(), It.IsAny<DateTime>(),
            It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Regression guard: ApiEmergencyStopPayload does NOT derive from ApiReasonPayload, so handling both
    // kinds with a single `is ApiReasonPayload` test would silently drop every stop event.
    [Fact]
    public async Task EmergencyStop_DoesNotLandOnTheResumeMethod()
    {
        var (device, _, store) = Build();

        device.OnApiEvent(Event(ApiEventKind.EmergencyStop,
            new ApiEmergencyStopPayload { Reason = "user-button" }));
        await DrainAsync(device);

        store.Verify(s => s.AddEmergencyResumeAsync(It.IsAny<ApiReasonPayload>(), It.IsAny<DateTime>(),
            It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);

        store.Verify(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(), It.IsAny<DateTime>(),
            It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreThrows_StillBroadcasts()
    {
        var (device, clients, store) = Build();
        store.Setup(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(), It.IsAny<DateTime>(),
                It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        device.OnApiEvent(Event(ApiEventKind.EmergencyStop,
            new ApiEmergencyStopPayload { Reason = "user-button" }));
        await DrainAsync(device);

        clients.Verify(c => c.EventReceived(It.IsAny<ApiEvent>()), Times.Once);
    }

    [Fact]
    public async Task MalformedPayload_IsIgnored_ButStillBroadcasts()
    {
        var (device, clients, store) = Build();

        var bad = new ApiEvent
        {
            Kind = ApiEventKind.EmergencyStop,
            When = 1_770_000_000,
            // reason should be a string; a JSON object cannot deserialize into one.
            Context = JsonSerializer.SerializeToElement(new { reason = new { nope = 1 } }, JsonDefaults.CamelCase)
        };

        device.OnApiEvent(bad);
        await DrainAsync(device);

        store.Verify(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(), It.IsAny<DateTime>(),
            It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);

        clients.Verify(c => c.EventReceived(It.IsAny<ApiEvent>()), Times.Once);
    }

    // ToUtc falls back to now when the producer timestamp is unset, rather than recording 1970.
    [Fact]
    public async Task MissingProducerTimestamp_FallsBackToNow()
    {
        var (device, _, store) = Build();
        var before = DateTime.UtcNow.AddSeconds(-1);

        device.OnApiEvent(Event(ApiEventKind.EmergencyStop,
            new ApiEmergencyStopPayload { Reason = "user-button" }, when: 0));
        await DrainAsync(device);

        store.Verify(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(),
            It.Is<DateTime>(d => d >= before), It.IsAny<long>(), It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // No pairing, no overwrite: each stop is its own row even with no resume between them.
    [Fact]
    public async Task TwoStopsWithNoResumeBetween_PersistTwice()
    {
        var (device, _, store) = Build();

        device.OnApiEvent(Event(ApiEventKind.EmergencyStop,
            new ApiEmergencyStopPayload { Reason = "alarm-monitor: DOORS_OPEN" }, index: 1));
        device.OnApiEvent(Event(ApiEventKind.EmergencyStop,
            new ApiEmergencyStopPayload { Reason = "RpcService" }, index: 2));
        await DrainAsync(device);

        store.Verify(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(), It.IsAny<DateTime>(),
            It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
