using System.Text.Json;
using Amazon.SimpleNotificationService;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Emergency;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AutoTrainer.Api.Tests.Emergency;

// The production worker creates its own SNS client, so the test double sits one level up: this subclass
// overrides the internal virtual publish, which is the only part that talks to AWS.
internal sealed class TestEmergencyWorker(
    AutotrainerDevice device, IOptions<MessageQueueOptions> options, bool publishSucceeds)
    : EmergencyWorker(new Mock<IEmergencyQueue>().Object, device, options,
        NullLogger<EmergencyWorker>.Instance)
{
    public int PublishCount { get; private set; }
    public string? LastSubject { get; private set; }
    public string? LastBody { get; private set; }

    internal override Task<bool> PublishToTopicAsync(IAmazonSimpleNotificationService client, string topicArn,
        string subject, string messageText)
    {
        PublishCount++;
        LastSubject = subject;
        LastBody = messageText;
        return Task.FromResult(publishSucceeds);
    }
}

public class EmergencyWorkerTests
{
    private static IOptions<MessageQueueOptions> Options(bool snsConfigured) =>
        Microsoft.Extensions.Options.Options.Create(new MessageQueueOptions
        {
            SnS = snsConfigured
                ? new SnSOptions { TopicArn = "arn:test", AccessKeyId = "key-id", AccessKey = "key" }
                : new SnSOptions()
        });

    private static AutotrainerDevice BuildDevice(IDeviceDataStore store)
    {
        var hub = new Mock<IHubContext<MessageHub, IMessageHub>>();
        var hubClients = new Mock<IHubClients<IMessageHub>>();
        hubClients.Setup(c => c.All).Returns(new Mock<IMessageHub>().Object);
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        return new AutotrainerDevice(
            new Mock<ICommandTaskQueue>().Object, hub.Object, NullLogger<AutotrainerDevice>.Instance,
            store, new Mock<IAnimalDataStore>().Object);
    }

    private static async Task DrainAsync(AutotrainerDevice device)
    {
        while (device.UpdateReader.TryRead(out var action))
        {
            await action();
        }
    }

    private static ApiEvent Event(ApiEventKind kind, object payload, ulong index = 4242) => new()
    {
        Kind = kind,
        When = 1_770_000_000,
        Index = index,
        Context = JsonSerializer.SerializeToElement(payload, JsonDefaults.CamelCase)
    };

    // ---- Alert composition -------------------------------------------------

    // The regression that motivated the change: a stop must report the alarms the event carried, not
    // whatever happens to be active when the worker runs. The live list is deliberately different.
    [Fact]
    public void BuildAlert_Stop_UsesPayloadAlarms_NotLiveSnapshot()
    {
        var payload = new ApiEmergencyStopPayload
        {
            Reason = "alarm-monitor: DOORS_OPEN",
            ActiveAlarms = [ApiAlarmKind.ExternalDoors]
        };

        var live = new List<Alarm> { new() { AlarmId = ApiAlarmKind.Thrashing, IsActive = true, IsEnabled = true } };

        var (subject, body) = EmergencyWorker.BuildAlert(
            Event(ApiEventKind.EmergencyStop, payload), payload, "device-7", live, []);

        Assert.Contains(nameof(ApiAlarmKind.ExternalDoors), body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ApiAlarmKind.Thrashing), body, StringComparison.Ordinal);
        Assert.Contains("at the emergency stop", body, StringComparison.Ordinal);
        Assert.Contains("alarm-monitor: DOORS_OPEN", subject, StringComparison.Ordinal);
    }

    // The resume payload carries no alarm list, so the live snapshot is all there is -- and it must be
    // labelled as current so a reader can tell it from an at-event list.
    [Fact]
    public void BuildAlert_Resume_UsesLiveSnapshot_LabelledAsCurrent()
    {
        var payload = new ApiReasonPayload { Reason = "alarm-monitor-resumed" };

        var live = new List<Alarm> { new() { AlarmId = ApiAlarmKind.Thrashing, IsActive = true, IsEnabled = true } };

        var (subject, body) = EmergencyWorker.BuildAlert(
            Event(ApiEventKind.EmergencyResume, payload), payload, "device-7", live, []);

        Assert.Contains(nameof(ApiAlarmKind.Thrashing), body, StringComparison.Ordinal);
        Assert.Contains("currently active", body, StringComparison.Ordinal);
        Assert.Contains("alarm-monitor-resumed", subject, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAlert_Stop_WithNoPayloadAlarms_SaysNoneWereActive()
    {
        var payload = new ApiEmergencyStopPayload { Reason = "user-button" };

        // A live alarm exists, but the stop carried none: the alert must report what the event said.
        var live = new List<Alarm> { new() { AlarmId = ApiAlarmKind.Thrashing, IsActive = true, IsEnabled = true } };

        var (_, body) = EmergencyWorker.BuildAlert(
            Event(ApiEventKind.EmergencyStop, payload), payload, "device-7", live, []);

        Assert.Contains("No alarm was active at the emergency stop", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ApiAlarmKind.Thrashing), body, StringComparison.Ordinal);
    }

    // ---- Notification stamp ------------------------------------------------

    [Fact]
    public async Task PublishSucceeds_StampsTheInsertedRow()
    {
        var store = new Mock<IDeviceDataStore>();
        store.Setup(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(), It.IsAny<DateTime>(),
                It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(77);

        var device = BuildDevice(store.Object);
        var worker = new TestEmergencyWorker(device, Options(snsConfigured: true), publishSucceeds: true);
        var stop = Event(ApiEventKind.EmergencyStop, new ApiEmergencyStopPayload { Reason = "user-button" });

        device.OnApiEvent(stop);                    // queues the insert, which records row id 77
        await worker.HandleEmergencyAsync(stop);    // queues the stamp
        await DrainAsync(device);

        Assert.Equal(1, worker.PublishCount);
        store.Verify(s => s.MarkNotificationSentAsync(77, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // A failed publish must leave no trace at all -- no stamp, and nothing pending for a later insert.
    [Fact]
    public async Task PublishFails_NothingIsStampedOrHeldPending()
    {
        var (factory, realStore) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var device = BuildDevice(realStore);
            var worker = new TestEmergencyWorker(device, Options(snsConfigured: true), publishSucceeds: false);
            var stop = Event(ApiEventKind.EmergencyStop, new ApiEmergencyStopPayload { Reason = "user-button" });

            await worker.HandleEmergencyAsync(stop);
            device.OnApiEvent(stop);
            await DrainAsync(device);

            using var db = factory.CreateDbContext();
            Assert.Null(db.EmergencyStopHistory.Single().NotificationSentAt);
        }
    }

    [Fact]
    public async Task SnsNotConfigured_DoesNotPublishOrStamp()
    {
        var store = new Mock<IDeviceDataStore>();
        var device = BuildDevice(store.Object);
        var worker = new TestEmergencyWorker(device, Options(snsConfigured: false), publishSucceeds: true);

        await worker.HandleEmergencyAsync(
            Event(ApiEventKind.EmergencyStop, new ApiEmergencyStopPayload { Reason = "user-button" }));
        await DrainAsync(device);

        Assert.Equal(0, worker.PublishCount);
        store.Verify(s => s.MarkNotificationSentAsync(It.IsAny<int>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- The rendezvous: both arrival orders converge ----------------------

    [Fact]
    public async Task InsertThenStamp_RowEndsUpStamped()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var device = BuildDevice(store);
            var worker = new TestEmergencyWorker(device, Options(snsConfigured: true), publishSucceeds: true);
            var stop = Event(ApiEventKind.EmergencyStop, new ApiEmergencyStopPayload { Reason = "user-button" });

            device.OnApiEvent(stop);
            await worker.HandleEmergencyAsync(stop);
            await DrainAsync(device);

            using var db = factory.CreateDbContext();
            Assert.NotNull(db.EmergencyStopHistory.Single().NotificationSentAt);
        }
    }

    // The case a FIFO-ordering assumption gets wrong: the notification is published and queued for stamping
    // before the event-topic copy has even been handled, so no row exists yet.
    [Fact]
    public async Task StampThenInsert_RowIsWrittenAlreadyStamped()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var device = BuildDevice(store);
            var worker = new TestEmergencyWorker(device, Options(snsConfigured: true), publishSucceeds: true);
            var stop = Event(ApiEventKind.EmergencyStop, new ApiEmergencyStopPayload { Reason = "user-button" });

            await worker.HandleEmergencyAsync(stop);   // queues the stamp before any row exists
            device.OnApiEvent(stop);                   // queues the insert
            await DrainAsync(device);                  // drain once, so the queued order is preserved

            using var db = factory.CreateDbContext();
            var row = db.EmergencyStopHistory.Single();

            Assert.NotNull(row.NotificationSentAt);
            // Written already stamped rather than inserted-then-updated: ApplyAudit would have bumped
            // UpdatedAt on any follow-up write.
            Assert.Equal(row.CreatedAt, row.UpdatedAt);
        }
    }

    // The correlation maps are capped, and overflow must not cost a delivered notification. Stale unmatched
    // entries pile up whenever the emergency queue drops an event (DropOldest, capacity 10), so a live pair
    // has to survive an overflow caused by them. Wholesale clearing on overflow would fail this.
    [Fact]
    public async Task CorrelationOverflow_DoesNotOrphanADeliveredNotification()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var device = BuildDevice(store);
            var worker = new TestEmergencyWorker(device, Options(snsConfigured: true), publishSucceeds: true);

            var live = Event(ApiEventKind.EmergencyStop,
                new ApiEmergencyStopPayload { Reason = "RpcService" }, index: 9_000);

            // The live event's row id is recorded FIRST -- this is the entry whose notification is still in
            // flight, and the one an overflow policy is most likely to discard.
            device.OnApiEvent(live);

            // Then a burst of other emergency events lands before the notification comes back, pushing the
            // map well past its sweep threshold. None of these ever gets a stamp (the emergency queue drops
            // events at capacity 10), so they are exactly the unmatched entries that accumulate in practice.
            for (ulong i = 1; i <= 50; i++)
            {
                device.OnApiEvent(Event(ApiEventKind.EmergencyStop,
                    new ApiEmergencyStopPayload { Reason = "user-button" }, i));
            }

            await worker.HandleEmergencyAsync(live);
            await DrainAsync(device);

            using var db = factory.CreateDbContext();

            var liveRow = db.EmergencyStopHistory.Single(r => r.EventIndex == 9_000);
            Assert.NotNull(liveRow.NotificationSentAt);

            // The unmatched ones are untouched -- nothing was stamped that should not have been.
            Assert.All(db.EmergencyStopHistory.Where(r => r.EventIndex != 9_000).ToList(),
                r => Assert.Null(r.NotificationSentAt));
        }
    }

    // No id is recorded when the insert fails, so the stamp has nothing to target and must not fall back to
    // matching an older row that happens to share the event's (Kind, EventIndex).
    [Fact]
    public async Task InsertFails_OlderDuplicateRowIsNotStamped()
    {
        var (factory, realStore) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            const ulong sharedIndex = 555;

            var olderId = await realStore.AddEmergencyStopAsync(
                new ApiEmergencyStopPayload { Reason = "user-button" },
                DateTime.UtcNow.AddMinutes(-5), (long)sharedIndex);

            var failing = new Mock<IDeviceDataStore>();
            failing.Setup(s => s.AddEmergencyStopAsync(It.IsAny<ApiEmergencyStopPayload>(), It.IsAny<DateTime>(),
                    It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("insert failed"));
            failing.Setup(s => s.MarkNotificationSentAsync(It.IsAny<int>(), It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns((int id, DateTime sent, CancellationToken ct) =>
                    realStore.MarkNotificationSentAsync(id, sent, ct));

            var device = BuildDevice(failing.Object);
            var worker = new TestEmergencyWorker(device, Options(snsConfigured: true), publishSucceeds: true);
            var stop = Event(ApiEventKind.EmergencyStop,
                new ApiEmergencyStopPayload { Reason = "user-button" }, sharedIndex);

            device.OnApiEvent(stop);                    // insert throws, so no id is recorded
            await worker.HandleEmergencyAsync(stop);
            await DrainAsync(device);

            failing.Verify(s => s.MarkNotificationSentAsync(It.IsAny<int>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()), Times.Never);

            using var db = factory.CreateDbContext();
            Assert.Null(db.EmergencyStopHistory.Single(r => r.Id == olderId).NotificationSentAt);
        }
    }
}
