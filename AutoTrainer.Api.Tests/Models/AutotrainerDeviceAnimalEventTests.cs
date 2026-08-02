using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Endpoints;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
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
    public async Task SessionEnded_BroadcastsSummaryAndSessionCount()
    {
        var (device, _, clients, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        var summary = new SessionSummaryDto(SessionA, null, null, false, 5, 4, 1);
        animalStore.Setup(s => s.ApplySessionEndedAsync("mouse-1", SessionA, It.IsAny<DateTime>(),
                5, 4, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);
        animalStore.Setup(s => s.CountSessionsAsync("mouse-1", It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        device.OnApiEvent(Event(ApiEventKind.SessionEnded, new ApiSessionEndedPayload
        {
            SessionId = SessionA, CaptureTrialCount = 5, AnalysisTrialCount = 4, FailedTrialCount = 1
        }));
        await DrainAsync(device);

        clients.Verify(c => c.SessionEnded(new SessionEnded("mouse-1", summary, 7)), Times.Once);
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

    [Theory]
    [InlineData(ApiEventKind.PelletPresentedCountChanged, ReachCountScope.Total, ReachCountField.PelletsPresented)]
    [InlineData(ApiEventKind.PelletConsumedCountChanged, ReachCountScope.Total, ReachCountField.PelletsConsumed)]
    [InlineData(ApiEventKind.ReachCountChanged, ReachCountScope.Total, ReachCountField.Reaches)]
    [InlineData(ApiEventKind.SuccessfulReachesCountChanged, ReachCountScope.Total, ReachCountField.SuccessfulReaches)]
    [InlineData(ApiEventKind.DayPelletPresentedCountChanged, ReachCountScope.Day, ReachCountField.PelletsPresented)]
    [InlineData(ApiEventKind.DayPelletConsumedCountChanged, ReachCountScope.Day, ReachCountField.PelletsConsumed)]
    [InlineData(ApiEventKind.DayReachCountChanged, ReachCountScope.Day, ReachCountField.Reaches)]
    [InlineData(ApiEventKind.DaySuccessfulReachesCountChanged, ReachCountScope.Day, ReachCountField.SuccessfulReaches)]
    public async Task CountChanged_RoutesToScopeAndColumn_UsingCount(ApiEventKind kind, ReachCountScope scope,
        ReachCountField field)
    {
        var (device, _, clients, _, animalStore) = Build();
        await SelectAnimalAsync(device);
        clients.Invocations.Clear();   // drop the AnimalChanged broadcast from selection

        // Change is deliberately different from Count to prove Count (the absolute value) is what's forwarded.
        device.OnApiEvent(Event(kind, new ApiCountChangePayload { Change = 1, Count = 42 }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyReachCountChangeAsync(
            "mouse-1", scope, field, 42, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once);

        // The live Animal model is updated between systemStatus messages, and the change is broadcast. (The
        // Animal is a single mutated reference, so assert its state directly rather than via the captured arg.)
        var live = scope == ReachCountScope.Total ? device.Animal!.ReachStatusTotal : device.Animal!.ReachStatusDay;
        Assert.Equal(42, Column(live, field));
        clients.Verify(c => c.AnimalChanged(It.IsAny<Animal>()), Times.Once);
    }

    private static double Column(ReachStatus s, ReachCountField field) => field switch
    {
        ReachCountField.PelletsPresented => s.PelletsPresented,
        ReachCountField.PelletsConsumed => s.PelletsConsumed,
        ReachCountField.Reaches => s.Reaches,
        ReachCountField.SuccessfulReaches => s.SuccessfulReaches,
        _ => double.NaN
    };

    [Fact]
    public async Task AnimalSelected_SeedsFiveDayReachStatusFromStore()
    {
        var (device, _, _, _, animalStore) = Build();
        animalStore.Setup(s => s.LoadFiveDayReachStatusAsync("mouse-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiveDayReachStatus(new DateOnly(2026, 7, 15),
                new ApiReachStatus { PelletsConsumed = 10 },   // prior four days
                new ApiReachStatus { PelletsConsumed = 3 }));  // current day

        await SelectAnimalAsync(device);

        Assert.Equal(13, device.Animal!.ReachStatus5Day.PelletsConsumed);   // 10 + 3
    }

    [Fact]
    public async Task DayCountChanged_UpdatesFiveDayRunningTotal_OnTopOfSeededPriorDays()
    {
        var (device, _, _, _, animalStore) = Build();
        animalStore.Setup(s => s.LoadFiveDayReachStatusAsync("mouse-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiveDayReachStatus(new DateOnly(2026, 7, 15),
                new ApiReachStatus { PelletsConsumed = 10 },
                new ApiReachStatus { PelletsConsumed = 3 }));
        await SelectAnimalAsync(device);

        // Day consumed count is now 8; the running total = 10 (prior four) + 8 (new current day).
        device.OnApiEvent(Event(ApiEventKind.DayPelletConsumedCountChanged, new ApiCountChangePayload { Count = 8 }));
        await DrainAsync(device);

        Assert.Equal(18, device.Animal!.ReachStatus5Day.PelletsConsumed);
    }

    [Fact]
    public async Task TotalCountChanged_DoesNotAffectFiveDay()
    {
        var (device, _, _, _, animalStore) = Build();
        animalStore.Setup(s => s.LoadFiveDayReachStatusAsync("mouse-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiveDayReachStatus(new DateOnly(2026, 7, 15),
                new ApiReachStatus { PelletsConsumed = 10 }, new ApiReachStatus { PelletsConsumed = 3 }));
        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.PelletConsumedCountChanged, new ApiCountChangePayload { Count = 999 }));
        await DrainAsync(device);

        Assert.Equal(13, device.Animal!.ReachStatus5Day.PelletsConsumed);   // unchanged by a total-scope event
    }

    [Fact]
    public async Task SystemStatus_DayRollover_ReseedsFiveDayFromNewDay()
    {
        var (device, _, _, _, animalStore) = Build();
        // Selection seeds day 15; the rollover to day 16 reseeds anchored at the new day (prior four only).
        animalStore.Setup(s => s.LoadFiveDayReachStatusAsync("mouse-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiveDayReachStatus(new DateOnly(2026, 7, 15),
                new ApiReachStatus { PelletsConsumed = 10 }, new ApiReachStatus { PelletsConsumed = 20 }));
        animalStore.Setup(s => s.LoadFiveDayReachStatusAsync("mouse-1", new DateOnly(2026, 7, 16),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiveDayReachStatus(new DateOnly(2026, 7, 16),
                new ApiReachStatus { PelletsConsumed = 12 }, new ApiReachStatus()));   // prior four, new day zero
        await SelectAnimalAsync(device);
        Assert.Equal(30, device.Animal!.ReachStatus5Day.PelletsConsumed);   // 10 + 20 on day 15

        device.OnApiEvent(Event(ApiEventKind.SystemStatus, new ApiSystemStatus
        {
            Animal = new ApiAnimalStatus { Identifier = "mouse-1" },   // new day has no consumed yet
            Project = new ApiProjectStatus { DayPath = "/data/mouse-1/20260716" }
        }));
        await DrainAsync(device);

        // Dropped day 11's 20-and-something; new day is zero -> total falls to the prior four (12).
        Assert.Equal(12, device.Animal!.ReachStatus5Day.PelletsConsumed);
    }

    // DayStarted.Date is read in local time (production converts the same way), so this is timezone-independent.
    private static DateOnly LocalDayOf(double epochSeconds) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds((long)(epochSeconds * 1000)).LocalDateTime);

    [Fact]
    public async Task DayStarted_SetsAttributionDay_ForSubsequentDayCountChanged()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        const double epoch = 1_781_000_000;   // whole seconds -> no rounding ambiguity
        var startedDay = LocalDayOf(epoch);

        device.OnApiEvent(Event(ApiEventKind.DayStarted, new ApiDayStartedPayload { Date = epoch }));
        device.OnApiEvent(Event(ApiEventKind.DayReachCountChanged, new ApiCountChangePayload { Count = 7 }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyReachCountChangeAsync(
            "mouse-1", ReachCountScope.Day, ReachCountField.Reaches, 7, startedDay, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DayCountChanged_Attribution_UsesNewerDayStarted_OverOlderDayPath()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        const double epoch = 1_781_000_000;
        var startedDay = LocalDayOf(epoch);
        var olderDayPath = startedDay.AddDays(-1);

        device.OnApiEvent(Event(ApiEventKind.DayStarted, new ApiDayStartedPayload { Date = epoch }));
        device.OnApiEvent(Event(ApiEventKind.SystemStatus, new ApiSystemStatus
        {
            Animal = new ApiAnimalStatus { Identifier = "mouse-1" },
            Project = new ApiProjectStatus { DayPath = $"/data/mouse-1/{olderDayPath:yyyyMMdd}" }
        }));
        device.OnApiEvent(Event(ApiEventKind.DayReachCountChanged, new ApiCountChangePayload { Count = 3 }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyReachCountChangeAsync(
            "mouse-1", ReachCountScope.Day, ReachCountField.Reaches, 3, startedDay, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DayCountChanged_Attribution_UsesNewerDayPath_OverOlderDayStarted()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);

        const double epoch = 1_781_000_000;
        var startedDay = LocalDayOf(epoch);
        var newerDayPath = startedDay.AddDays(1);

        device.OnApiEvent(Event(ApiEventKind.DayStarted, new ApiDayStartedPayload { Date = epoch }));
        device.OnApiEvent(Event(ApiEventKind.SystemStatus, new ApiSystemStatus
        {
            Animal = new ApiAnimalStatus { Identifier = "mouse-1" },
            Project = new ApiProjectStatus { DayPath = $"/data/mouse-1/{newerDayPath:yyyyMMdd}" }
        }));
        device.OnApiEvent(Event(ApiEventKind.DayReachCountChanged, new ApiCountChangePayload { Count = 3 }));
        await DrainAsync(device);

        animalStore.Verify(s => s.ApplyReachCountChangeAsync(
            "mouse-1", ReachCountScope.Day, ReachCountField.Reaches, 3, newerDayPath, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CurrentDeviceDay_IsNullUntilDayIsKnown_ThenTracksDayPath()
    {
        var (device, _, _, _, _) = Build();
        Assert.Null(device.CurrentDeviceDay);   // no DayStarted / systemStatus yet

        device.OnApiEvent(Event(ApiEventKind.SystemStatus, new ApiSystemStatus
        {
            Animal = new ApiAnimalStatus { Identifier = "mouse-1" },
            Project = new ApiProjectStatus { DayPath = "/data/mouse-1/20260718" }
        }));
        await DrainAsync(device);

        Assert.Equal(new DateOnly(2026, 7, 18), device.CurrentDeviceDay);
    }

    [Fact]
    public async Task GetAnimalReachStatus_NoAnimalSelected_Returns404()
    {
        var (device, _, _, _, _) = Build();

        var result = await AnimalEndpoints.GetAnimalReachStatus(
            animal: null, device, store: null!, storage: null!, ct: CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetAnimalReachStatus_PassesCurrentDeviceDay_AndReturnsStoreResult()
    {
        var (device, _, _, _, animalStore) = Build();
        await SelectAnimalAsync(device);   // selects mouse-1
        device.OnApiEvent(Event(ApiEventKind.SystemStatus, new ApiSystemStatus
        {
            Animal = new ApiAnimalStatus { Identifier = "mouse-1" },
            Project = new ApiProjectStatus { DayPath = "/data/mouse-1/20260718" }
        }));
        await DrainAsync(device);

        var expected = new AnimalReachStatusDto(
            new ReachStatusSnapshotDto(10, 9, 8, 7, null),
            new ReachStatusSnapshotDto(3, 2, 1, 0, new DateOnly(2026, 7, 18)));
        animalStore.Setup(s => s.GetReachStatusAsync("mouse-1", new DateOnly(2026, 7, 18), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await AnimalEndpoints.GetAnimalReachStatus(
            animal: null, device, animalStore.Object, storage: null!, ct: CancellationToken.None);

        var ok = Assert.IsType<Ok<AnimalReachStatusDto>>(result);
        Assert.Same(expected, ok.Value);
        animalStore.Verify(s => s.GetReachStatusAsync("mouse-1", new DateOnly(2026, 7, 18),
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
