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
    private const string SessionA = "11111111-1111-4111-8111-111111111111";
    private const string BatchA = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

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

    private static ApiEvent Event(ApiEventKind kind, object? payload) => new()
    {
        Kind = kind,
        When = 1_770_000_000,
        Context = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonDefaults.CamelCase)
    };

    // The animal is established out of band; every animal-database write depends on it being selected.
    private static async Task SelectAnimalAsync(AutotrainerDevice device, string identifier = "mouse-1")
    {
        device.OnApiEvent(Event(ApiEventKind.AnimalSelected, new ApiAnimalStatus { Identifier = identifier }));
        await DrainAsync(device);
    }

    [Fact]
    public async Task AnimalSelected_PersistsAnimalHistory()
    {
        var (device, _, _, _, animalStore) = Build();

        device.OnApiEvent(Event(ApiEventKind.AnimalSelected,
            new ApiAnimalStatus { Identifier = "mouse-1", Name = "A" }));
        await DrainAsync(device);

        animalStore.Verify(s => s.AddAnimalHistoryAsync(
            It.Is<ApiAnimalStatus>(a => a.Identifier == "mouse-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnimalSelected_EmptyIdentifier_DoesNotPersist()
    {
        var (device, _, _, _, animalStore) = Build();

        device.OnApiEvent(Event(ApiEventKind.AnimalSelected,
            new ApiAnimalStatus { Identifier = "", Name = "A" }));
        await DrainAsync(device);

        animalStore.Verify(s => s.AddAnimalHistoryAsync(
            It.IsAny<ApiAnimalStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnimalSelected_StoreThrows_StillBroadcasts()
    {
        var (device, _, clients, _, animalStore) = Build();
        animalStore.Setup(s => s.AddAnimalHistoryAsync(It.IsAny<ApiAnimalStatus>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        device.OnApiEvent(Event(ApiEventKind.AnimalSelected, new ApiAnimalStatus { Identifier = "mouse-1" }));
        await DrainAsync(device);

        clients.Verify(c => c.EventReceived(It.IsAny<ApiEvent>()), Times.Once);
    }

    // Sessions can run with no animal involved. That is normal: the event is simply not stored.
    [Fact]
    public async Task SessionStarted_NoAnimalSelected_DoesNotPersist_ButStillBroadcasts()
    {
        var (device, _, clients, _, animalStore) = Build();

        device.OnApiEvent(Event(ApiEventKind.SessionStarted,
            new ApiSessionStartedPayload { SessionId = SessionA, IsAnalysisDeferred = true }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplySessionStartedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);

        clients.Verify(c => c.EventReceived(It.IsAny<ApiEvent>()), Times.Once);
    }

    [Fact]
    public async Task SessionStarted_PersistsWithPayloadSessionId()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.SessionStarted,
            new ApiSessionStartedPayload { SessionId = SessionA, IsAnalysisDeferred = true }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplySessionStartedAsync(
            "mouse-1", SessionA, It.IsAny<DateTime>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SessionEnded_PersistsCounts()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.SessionEnded, new ApiSessionEndedPayload
        {
            SessionId = SessionA,
            CaptureTrialCount = 5,
            AnalysisTrialCount = 4,
            FailedTrialCount = 1
        }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplySessionEndedAsync(
            "mouse-1", SessionA, It.IsAny<DateTime>(), 5, 4, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchAnalysisEnded_PersistsBatchIdAndCounts()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.BatchAnalysisEnded, new ApiBatchAnalysisEndedPayload
        {
            SessionId = SessionA,
            BatchId = BatchA,
            AnalysisTrialCount = 4,
            FailedTrialCount = 1
        }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyBatchAnalysisEndedAsync(
            "mouse-1", SessionA, BatchA, It.IsAny<DateTime>(), 4, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TrialStarted_PersistsReason()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.TrialStarted,
            new ApiTrialStartedPayload { SessionId = SessionA, TrialId = 3, Reason = "pellet" }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyTrialEventAsync("mouse-1", ApiEventKind.TrialStarted,
            It.Is<TrialEventValues>(v => v.SessionId == SessionA && v.TrialId == 3 && v.Reason == "pellet"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IntertrialSave_PassesBatchIdAndLocation()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.IntertrialDetectionSave, new ApiIntertrialSavePayload
        {
            SessionId = SessionA,
            TrialId = 3,
            BatchId = BatchA,
            Location = "/out/det.h5"
        }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyTrialEventAsync("mouse-1", ApiEventKind.IntertrialDetectionSave,
            It.Is<TrialEventValues>(v => v.BatchId == BatchA && v.Location == "/out/det.h5"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // batch_id on the first-sighting events is deprecated and meaningless -- reading it would create a
    // bogus BatchAnalysis row, so it must not reach the store.
    [Fact]
    public async Task TrialSeen_DoesNotForwardDeprecatedBatchId()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.TrialAnimalSeen, new ApiTrialSeenPayload
        {
            SessionId = SessionA,
            TrialId = 3,
            BatchId = BatchA        // present on the wire, must be ignored
        }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyTrialEventAsync("mouse-1", ApiEventKind.TrialAnimalSeen,
            It.Is<TrialEventValues>(v => v.BatchId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TrialReachEvents_ReplacesByTrial()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.TrialReachEvents, new ApiTrialReachEventsPayload
        {
            SessionId = SessionA,
            TrialId = 3,
            BatchId = BatchA,
            TrialReachEvents = [new ReachEvent { Init = 1 }]
        }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ReplaceTrialReachEventsAsync(
            "mouse-1", SessionA, 3, BatchA,
            It.Is<IReadOnlyCollection<ReachEvent>>(r => r.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Tunnel events still drive system state and reach the hub, but no longer define a session.
    [Fact]
    public async Task TunnelEvents_DoNotPersistAnything()
    {
        var (device, _, clients, _, animalStore) = Build();
        await SelectAnimalAsync(device);
        animalStore.Invocations.Clear();

        device.OnApiEvent(Event(ApiEventKind.TunnelEnter, null));
        device.OnApiEvent(Event(ApiEventKind.TunnelExit, null));
        await DrainAsync(device);

        Assert.Empty(animalStore.Invocations);
        clients.Verify(c => c.SystemStateChanged(It.IsAny<SystemState>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AnimalSelected_WithNullPayload_ClearsSelection_SoLaterEventsAreNotStored()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.AnimalSelected, null));
        await DrainAsync(device);

        Assert.Null(device.Animal);

        device.OnApiEvent(Event(ApiEventKind.SessionStarted,
            new ApiSessionStartedPayload { SessionId = SessionA }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplySessionStartedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SystemStatus_PersistsReachStatusWithDayFromDayPath()
    {
        var (device, _, _, _, animalStore) = Build();

        var status = new ApiSystemStatus
        {
            Animal = new ApiAnimalStatus
            {
                Identifier = "mouse-1",
                ReachStatusTotal = new ApiReachStatus { PelletsPresented = 10 },
                ReachStatusDay = new ApiReachStatus { PelletsPresented = 3 }
            },
            Project = new ApiProjectStatus { DayPath = "/data/mouse-1/20260713" }
        };

        device.OnApiEvent(Event(ApiEventKind.SystemStatus, status));
        await DrainAsync(device);

        animalStore.Verify(s => s.AddReachStatusIfChangedAsync(
            "mouse-1",
            It.Is<ApiReachStatus>(r => r.PelletsPresented == 10),
            It.Is<ApiReachStatus>(r => r.PelletsPresented == 3),
            new DateOnly(2026, 7, 13),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Regression: this event's payload is IsEnabledContext ("isEnabled"), but it used to be deserialized as
    // ApiIsEngagedPayload ("isEngaged"). The field never matched, so LoadCellEnabled was always false.
    [Fact]
    public async Task HeadfixLoadCellEnabledChanged_ReadsIsEnabled()
    {
        var (device, _, _, _, _) = Build();

        device.OnApiEvent(Event(ApiEventKind.HeadfixLoadCellEnabledChanged,
            new ApiIsEnabledPayload { IsEnabled = true }));
        await DrainAsync(device);

        Assert.True(device.Behavior.LoadCellEnabled);
    }

    // A malformed payload must not take the event down: it is logged and the broadcast still happens.
    [Fact]
    public async Task MalformedPayload_IsIgnored_AndEventStillBroadcasts()
    {
        var (device, _, clients, _, animalStore) = Build();
        await SelectAnimalAsync(device);
        clients.Invocations.Clear();   // the animal selection broadcast one of its own

        var bad = new ApiEvent
        {
            Kind = ApiEventKind.SessionStarted,
            When = 1_770_000_000,
            // sessionId should be a string; a JSON object cannot deserialize into one.
            Context = JsonSerializer.SerializeToElement(new { sessionId = new { nope = 1 } }, JsonDefaults.CamelCase)
        };

        device.OnApiEvent(bad);
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplySessionStartedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);

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
