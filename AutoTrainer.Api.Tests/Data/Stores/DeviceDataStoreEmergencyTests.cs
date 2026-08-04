using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Endpoints;
using AutoTrainer.Api.Models;
using Xunit;

namespace AutoTrainer.Api.Tests.Data.Stores;

public class DeviceDataStoreEmergencyTests
{
    private static readonly DateTime Occurred = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    private static readonly PageRequest FirstPage = PageRequest.From(null, null);

    private static ApiEmergencyStopPayload Stop(string reason, params ApiAlarmKind[] alarms) =>
        new() { Reason = reason, ActiveAlarms = [.. alarms] };

    [Fact]
    public async Task AddEmergencyStop_MapsEveryColumn()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var id = await store.AddEmergencyStopAsync(
                Stop("alarm-monitor: DOORS_OPEN", ApiAlarmKind.ExternalDoors, ApiAlarmKind.SystemFault),
                Occurred, eventIndex: 4242);

            using var db = factory.CreateDbContext();
            var row = Assert.Single(db.EmergencyStopHistory.ToList());

            Assert.Equal(id, row.Id);
            Assert.Equal(ApiEventKind.EmergencyStop, row.Kind);
            Assert.Equal(Occurred, row.OccurredAt);
            Assert.Equal(4242, row.EventIndex);
            Assert.Equal(ApiEmergencyStopReason.AlarmMonitor, row.StopReasonId);
            Assert.Null(row.ResumeReasonId);
            Assert.Equal("alarm-monitor: DOORS_OPEN", row.ReasonText);   // verbatim, suffix intact
            Assert.Null(row.NotificationSentAt);
        }
    }

    // Integer ids, order preserved -- the stored format must not depend on serializer configuration.
    [Fact]
    public async Task AddEmergencyStop_SerializesAlarmIdsAsIntegers()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddEmergencyStopAsync(
                Stop("user-button", ApiAlarmKind.ExternalDoors, ApiAlarmKind.SystemFault),
                Occurred, eventIndex: 1);

            using var db = factory.CreateDbContext();
            Assert.Equal("[101,501]", db.EmergencyStopHistory.Single().ActiveAlarms);
        }
    }

    // "[]" and null mean different things: the stop carried no alarms, versus the kind has no such field.
    [Fact]
    public async Task EmptyAlarmList_StoresEmptyArray_ResumeStoresNull()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddEmergencyStopAsync(Stop("user-button"), Occurred, eventIndex: 1);
            await store.AddEmergencyResumeAsync(
                new ApiEmergencyResumePayload { Reason = "user-button" }, Occurred, eventIndex: 2);

            using var db = factory.CreateDbContext();
            var rows = db.EmergencyStopHistory.OrderBy(r => r.Id).ToList();

            Assert.Equal("[]", rows[0].ActiveAlarms);
            Assert.Null(rows[1].ActiveAlarms);
        }
    }

    [Fact]
    public async Task AddEmergencyResume_SetsResumeReasonOnly()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddEmergencyResumeAsync(
                new ApiEmergencyResumePayload { Reason = "alarm-monitor-resumed" }, Occurred, eventIndex: 7);

            using var db = factory.CreateDbContext();
            var row = db.EmergencyStopHistory.Single();

            Assert.Equal(ApiEventKind.EmergencyResume, row.Kind);
            Assert.Equal(ApiEmergencyResumeReason.AlarmMonitorResume, row.ResumeReasonId);
            Assert.Null(row.StopReasonId);
        }
    }

    // An unrecognized string must never be lost -- that is the whole reason ReasonText exists.
    [Fact]
    public async Task UnrecognizedReason_StoresUnknownAndKeepsRawText()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddEmergencyStopAsync(Stop("brand-new-producer-reason"), Occurred, eventIndex: 1);

            using var db = factory.CreateDbContext();
            var row = db.EmergencyStopHistory.Single();

            Assert.Equal(ApiEmergencyStopReason.Unknown, row.StopReasonId);
            Assert.Equal("brand-new-producer-reason", row.ReasonText);
        }
    }

    // The stamp-first arrival order: the row is written already stamped, in one write.
    [Fact]
    public async Task NotificationSentAt_CanBeSetAtInsert()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var sent = new DateTime(2026, 8, 3, 12, 0, 5, DateTimeKind.Utc);

            await store.AddEmergencyStopAsync(Stop("user-button"), Occurred, 1, notificationSentAt: sent);

            using var db = factory.CreateDbContext();
            var row = db.EmergencyStopHistory.Single();

            Assert.Equal(sent, row.NotificationSentAt);
            Assert.Equal(row.CreatedAt, row.UpdatedAt);   // inserted stamped, never updated afterwards
        }
    }

    [Fact]
    public async Task MarkNotificationSent_StampsTheRow()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var id = await store.AddEmergencyStopAsync(Stop("user-button"), Occurred, 1);
            var sent = new DateTime(2026, 8, 3, 12, 0, 5, DateTimeKind.Utc);

            Assert.True(await store.MarkNotificationSentAsync(id, sent));

            using var db = factory.CreateDbContext();
            Assert.Equal(sent, db.EmergencyStopHistory.Single().NotificationSentAt);
        }
    }

    [Fact]
    public async Task MarkNotificationSent_UnknownId_ReturnsFalse()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            Assert.False(await store.MarkNotificationSentAsync(9999, DateTime.UtcNow));
        }
    }

    // Genuine idempotence: a re-submitted stamp preserves the original send time rather than overwriting it.
    [Fact]
    public async Task MarkNotificationSent_IsIdempotent()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var id = await store.AddEmergencyStopAsync(Stop("user-button"), Occurred, 1);
            var first = new DateTime(2026, 8, 3, 12, 0, 5, DateTimeKind.Utc);
            var second = new DateTime(2026, 8, 3, 12, 9, 9, DateTimeKind.Utc);

            Assert.True(await store.MarkNotificationSentAsync(id, first));
            Assert.False(await store.MarkNotificationSentAsync(id, second));

            using var db = factory.CreateDbContext();
            Assert.Equal(first, db.EmergencyStopHistory.Single().NotificationSentAt);
        }
    }

    // ---- Read path ---------------------------------------------------------

    [Fact]
    public async Task GetEmergencies_FiltersByKind_Pages_AndNewestFirst()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var now = DateTime.UtcNow;

            await store.AddEmergencyStopAsync(Stop("user-button"), now.AddMinutes(-3), 1);
            await store.AddEmergencyResumeAsync(
                new ApiEmergencyResumePayload { Reason = "user-button" }, now.AddMinutes(-2), 2);
            await store.AddEmergencyStopAsync(Stop("RpcService"), now.AddMinutes(-1), 3);

            var all = await store.GetEmergenciesAsync(now.AddDays(-1), [], [], [], FirstPage);
            Assert.Equal(3, all.TotalCount);
            Assert.Equal(ApiEmergencyStopReason.RpcService, all.Items[0].StopReasonId);   // newest first

            var stops = await store.GetEmergenciesAsync(now.AddDays(-1), [ApiEventKind.EmergencyStop], [], [], FirstPage);
            Assert.Equal(2, stops.TotalCount);
            Assert.All(stops.Items, e => Assert.Equal(ApiEventKind.EmergencyStop, e.Kind));

            // Paging returns a short page but the total still counts every match.
            var paged = await store.GetEmergenciesAsync(now.AddDays(-1), [], [], [], new PageRequest(1, 2));
            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(3, paged.TotalCount);
        }
    }

    [Fact]
    public async Task GetEmergencies_FiltersByStopReason()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var now = DateTime.UtcNow;

            await store.AddEmergencyStopAsync(Stop("alarm-monitor: DOORS_OPEN"), now.AddMinutes(-3), 1);
            await store.AddEmergencyStopAsync(Stop("user-button"), now.AddMinutes(-2), 2);
            await store.AddEmergencyStopAsync(Stop("RpcService"), now.AddMinutes(-1), 3);

            var page = await store.GetEmergenciesAsync(now.AddDays(-1), [ApiEventKind.EmergencyStop],
                [ApiEmergencyStopReason.AlarmMonitor, ApiEmergencyStopReason.RpcService], [], FirstPage);

            Assert.Equal(2, page.TotalCount);
            Assert.All(page.Items, e =>
                Assert.Contains(e.StopReasonId, new[]
                {
                    (ApiEmergencyStopReason?)ApiEmergencyStopReason.AlarmMonitor,
                    ApiEmergencyStopReason.RpcService
                }));
        }
    }

    [Fact]
    public async Task GetEmergencies_FiltersByResumeReason()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var now = DateTime.UtcNow;

            await store.AddEmergencyResumeAsync(
                new ApiEmergencyResumePayload { Reason = "alarm-monitor-resumed" }, now.AddMinutes(-2), 1);
            await store.AddEmergencyResumeAsync(
                new ApiEmergencyResumePayload { Reason = "user-button" }, now.AddMinutes(-1), 2);

            var page = await store.GetEmergenciesAsync(now.AddDays(-1), [ApiEventKind.EmergencyResume],
                [], [ApiEmergencyResumeReason.AlarmMonitorResume], FirstPage);

            var item = Assert.Single(page.Items);
            Assert.Equal(ApiEmergencyResumeReason.AlarmMonitorResume, item.ResumeReasonId);
        }
    }

    // The window is applied to OccurredAt (the producer's event time), not to the row's write time -- every
    // row here is written now, so a CreatedAt-based filter would return both.
    [Fact]
    public async Task GetEmergencies_WindowsOnOccurredAt_NotWriteTime()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var now = DateTime.UtcNow;

            await store.AddEmergencyStopAsync(Stop("user-button"), now.AddMinutes(-1), 1);
            await store.AddEmergencyStopAsync(Stop("user-button"), now.AddDays(-10), 2);

            var recent = await store.GetEmergenciesAsync(now.AddDays(-5), [], [], [], FirstPage);
            Assert.Equal(1, recent.TotalCount);
        }
    }

    // The column holds JSON; the DTO must expose a real array, and null must stay null on resume rows.
    [Fact]
    public async Task GetEmergencies_ProjectsActiveAlarmsAsArray()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var now = DateTime.UtcNow;

            await store.AddEmergencyStopAsync(
                Stop("alarm-monitor: DOORS_OPEN", ApiAlarmKind.ExternalDoors, ApiAlarmKind.SystemFault),
                now.AddMinutes(-3), 1);
            await store.AddEmergencyStopAsync(Stop("user-button"), now.AddMinutes(-2), 2);
            await store.AddEmergencyResumeAsync(
                new ApiEmergencyResumePayload { Reason = "user-button" }, now.AddMinutes(-1), 3);

            var page = await store.GetEmergenciesAsync(now.AddDays(-1), [], [], [], FirstPage);

            Assert.Null(page.Items[0].ActiveAlarms);                       // resume: no such field
            Assert.Empty(page.Items[1].ActiveAlarms!);                     // stop with none active
            Assert.Equal([ApiAlarmKind.ExternalDoors, ApiAlarmKind.SystemFault], page.Items[2].ActiveAlarms!);
        }
    }

    [Fact]
    public async Task GetEmergencies_SurfacesReasonTextAndNotificationStamp()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var now = DateTime.UtcNow;
            var sent = now.AddSeconds(-30);

            var id = await store.AddEmergencyStopAsync(
                Stop("alarm-monitor: DOORS_OPEN SYSTEM_FAULT"), now.AddMinutes(-1), 1);
            await store.MarkNotificationSentAsync(id, sent);

            var item = Assert.Single((await store.GetEmergenciesAsync(now.AddDays(-1), [], [], [], FirstPage)).Items);

            // The enum collapses the token suffix; ReasonText is how a caller recovers it.
            Assert.Equal(ApiEmergencyStopReason.AlarmMonitor, item.StopReasonId);
            Assert.Equal("alarm-monitor: DOORS_OPEN SYSTEM_FAULT", item.ReasonText);
            Assert.Null(item.ResumeReasonId);
            Assert.Equal(sent, item.NotificationSentAt!.Value, TimeSpan.FromSeconds(1));
        }
    }

    // EventIndex is explicitly not unique. Stamping by primary key means a collision can never reach back
    // to an earlier event -- this pins that property.
    [Fact]
    public async Task DuplicateEventIndex_StampingOneLeavesTheOtherAlone()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            const long sharedIndex = 555;

            var olderId = await store.AddEmergencyStopAsync(Stop("user-button"), Occurred, sharedIndex);
            var newerId = await store.AddEmergencyStopAsync(Stop("user-button"), Occurred, sharedIndex);

            await store.MarkNotificationSentAsync(newerId, DateTime.UtcNow);

            using var db = factory.CreateDbContext();
            Assert.Null(db.EmergencyStopHistory.Single(r => r.Id == olderId).NotificationSentAt);
            Assert.NotNull(db.EmergencyStopHistory.Single(r => r.Id == newerId).NotificationSentAt);
        }
    }
}
