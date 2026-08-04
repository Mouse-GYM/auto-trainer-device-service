using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

// Endpoint-owned behavior only (request parsing + result type). The resolution/paging/filter logic itself is
// covered by the helper and store tests. AutotrainerDevice is not constructed here (its state is not settable
// from a unit test); the /reaches resolution path is covered by AnimalResolutionTests + AnimalDataStoreTests.
public class EndpointHandlerTests
{
    [Fact]
    public void GetApiEventHistory_ValidRequest_Returns404()
    {
        var result = DeviceEndpoints.GetApiEventHistory(within: "24h", kind: ["SessionStarted"], page: null, pageSize: null);
        Assert.IsType<NotFound<string>>(result);
    }

    [Fact]
    public void GetApiEventHistory_BadWindow_Returns400()
    {
        var result = DeviceEndpoints.GetApiEventHistory(within: "banana", kind: null, page: null, pageSize: null);
        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public void GetApiEventHistory_InvalidKind_Returns400()
    {
        var result = DeviceEndpoints.GetApiEventHistory(within: null, kind: ["NotAnEvent"], page: null, pageSize: null);
        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task GetAlarms_BadWindow_Returns400()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var result = await DeviceEndpoints.GetAlarms("banana", null, null, null, null, store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(result);
        }
    }

    [Fact]
    public async Task GetAlarms_InvalidAlarmId_Returns400()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var result = await DeviceEndpoints.GetAlarms(null, ["NotAKind"], null, null, null, store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(result);
        }
    }

    [Fact]
    public async Task GetTrials_UnsupportedSortField_Returns400()
    {
        // Sort validation runs before any resolution/store access, so the collaborators are never touched.
        var result = await SessionEndpoints.GetTrials("session", animal: null, analysisPerformed: null,
            reachMethod: null, reachOutcome: null, sort: "bogus", page: null, pageSize: null,
            device: null!, store: null!, storage: null!, ct: CancellationToken.None);
        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task GetSessionsCount_BadWindow_Returns400()
    {
        // Window parsing runs before any resolution/store access, so the collaborators are never touched.
        var result = await SessionEndpoints.GetSessionsCount(within: "banana", animal: null,
            isAnalysisDeferred: null, device: null!, store: null!, storage: null!, ct: CancellationToken.None);
        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task GetReachesCount_BadWindow_Returns400()
    {
        var result = await ReachEndpoints.GetReachesCount(within: "banana", animal: null, method: null,
            outcome: null, device: null!, store: null!, storage: null!, ct: CancellationToken.None);
        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task GetSession_UnsupportedSortField_Returns400()
    {
        // Expand + sort validation run before any resolution/store access, so the collaborators are never touched.
        var result = await SessionEndpoints.GetSession("session", animal: null, expand: "trials", sort: "bogus",
            device: null!, store: null!, storage: null!, ct: CancellationToken.None);
        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task GetEmergencies_BadWindow_Returns400()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var result = await DeviceEndpoints.GetEmergencies("banana", null, null, null, null, store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(result);
        }
    }

    [Fact]
    public async Task GetEmergencies_InvalidKind_Returns400()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var result = await DeviceEndpoints.GetEmergencies(null, ["NotAnEvent"], null, null, null, store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(result);
        }
    }

    // A real ApiEventKind that this table never holds: rejected rather than silently returning an empty page.
    [Fact]
    public async Task GetEmergencies_NonEmergencyKind_Returns400()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var result = await DeviceEndpoints.GetEmergencies(null, ["SessionStarted"], null, null, null, store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(result);
        }
    }

    [Fact]
    public async Task GetEmergencies_Happy_ReturnsOkEnvelope()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddEmergencyStopAsync(
                new ApiEmergencyStopPayload { Reason = "user-button", ActiveAlarms = [ApiAlarmKind.ExternalDoors] },
                DateTime.UtcNow.AddMinutes(-1), eventIndex: 1);

            var result = await DeviceEndpoints.GetEmergencies(null, ["EmergencyStop"], null, null, null, store,
                CancellationToken.None);

            var ok = Assert.IsType<Ok<PagedResult<EmergencyDto>>>(result);
            Assert.Equal(1, ok.Value!.TotalCount);
            Assert.Equal(ApiEventKind.EmergencyStop, ok.Value.Items[0].Kind);
            Assert.Equal([ApiAlarmKind.ExternalDoors], ok.Value.Items[0].ActiveAlarms!);
        }
    }

    // Endpoint-level pass-through of the new filter; the query itself is covered by DeviceDataStoreTests.
    [Fact]
    public async Task GetAlarms_IsEnabledIsPassedThrough()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddAlarmHistoryAsync(new ApiAlarmStatus
            {
                AlarmId = ApiAlarmKind.AnimalMissing, IsActive = true, IsEnabled = true
            });
            await store.AddAlarmHistoryAsync(new ApiAlarmStatus
            {
                AlarmId = ApiAlarmKind.ExternalDoors, IsActive = true, IsEnabled = false
            });

            var enabled = await DeviceEndpoints.GetAlarms(null, null, true, null, null, store, CancellationToken.None);
            var ok = Assert.IsType<Ok<PagedResult<AlarmDto>>>(enabled);
            Assert.Equal(1, ok.Value!.TotalCount);
            Assert.True(ok.Value.Items[0].IsEnabled);

            // Omitted means either, so the unfiltered call is unaffected by the new parameter.
            var either = await DeviceEndpoints.GetAlarms(null, null, null, null, null, store, CancellationToken.None);
            Assert.Equal(2, Assert.IsType<Ok<PagedResult<AlarmDto>>>(either).Value!.TotalCount);
        }
    }

    [Fact]
    public async Task GetDetectors_IsEnabledIsPassedThrough()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddDetectorHistoryAsync(new ApiDetectorStatus
            {
                DetectorId = ApiDetectorKind.FrontDoor, IsActive = true, IsEnabled = true
            });
            await store.AddDetectorHistoryAsync(new ApiDetectorStatus
            {
                DetectorId = ApiDetectorKind.SlidingDoor, IsActive = true, IsEnabled = false
            });

            var disabled = await DeviceEndpoints.GetDetectors(null, null, false, null, null, store,
                CancellationToken.None);
            var ok = Assert.IsType<Ok<PagedResult<DetectorDto>>>(disabled);
            Assert.Equal(1, ok.Value!.TotalCount);
            Assert.False(ok.Value.Items[0].IsEnabled);
        }
    }

    // Seeds one stop (user-button) and one resume (alarm-monitor-resumed), both recent.
    private static async Task SeedTwoEmergenciesAsync(AutoTrainer.Api.Data.Stores.DeviceDataStore store)
    {
        await store.AddEmergencyStopAsync(new ApiEmergencyStopPayload { Reason = "user-button" },
            DateTime.UtcNow.AddMinutes(-2), eventIndex: 1);
        await store.AddEmergencyResumeAsync(new ApiReasonPayload { Reason = "alarm-monitor-resumed" },
            DateTime.UtcNow.AddMinutes(-1), eventIndex: 2);
    }

    [Fact]
    public async Task GetEmergencies_CodeFiltersWithinASingleKind()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await SeedTwoEmergenciesAsync(store);

            var match = await DeviceEndpoints.GetEmergencies(null, ["EmergencyStop"], ["UserButton"], null, null,
                store, CancellationToken.None);
            Assert.Equal(1, Assert.IsType<Ok<PagedResult<EmergencyDto>>>(match).Value!.TotalCount);

            // Same numeric code, but 201 on the resume side is also UserButton -- and that resume row's reason
            // is AlarmMonitorResumed, so it must not match.
            var noMatch = await DeviceEndpoints.GetEmergencies(null, ["EmergencyResume"], ["201"], null, null,
                store, CancellationToken.None);
            Assert.Equal(0, Assert.IsType<Ok<PagedResult<EmergencyDto>>>(noMatch).Value!.TotalCount);
        }
    }

    // A code is only meaningful once the kind narrows to one direction, so when the request returns both it is
    // ignored outright -- not applied, and not even validated.
    [Fact]
    public async Task GetEmergencies_CodeIsIgnoredWhenAllKindsAreReturned()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await SeedTwoEmergenciesAsync(store);

            var noKind = await DeviceEndpoints.GetEmergencies(null, null, ["UserButton"], null, null,
                store, CancellationToken.None);
            Assert.Equal(2, Assert.IsType<Ok<PagedResult<EmergencyDto>>>(noKind).Value!.TotalCount);

            // Asking for both kinds explicitly is the same as asking for all of them.
            var bothKinds = await DeviceEndpoints.GetEmergencies(null, ["EmergencyStop", "EmergencyResume"],
                ["UserButton"], null, null, store, CancellationToken.None);
            Assert.Equal(2, Assert.IsType<Ok<PagedResult<EmergencyDto>>>(bothKinds).Value!.TotalCount);

            // Ignored means ignored: even a nonsense code is not rejected when there is no enum to read it against.
            var nonsense = await DeviceEndpoints.GetEmergencies(null, null, ["NotAReason"], null, null,
                store, CancellationToken.None);
            Assert.Equal(2, Assert.IsType<Ok<PagedResult<EmergencyDto>>>(nonsense).Value!.TotalCount);
        }
    }

    [Fact]
    public async Task GetEmergencies_InvalidCodeForASingleKind_Returns400()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var badName = await DeviceEndpoints.GetEmergencies(null, ["EmergencyStop"], ["NotAReason"], null, null,
                store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(badName);

            // 999 is not a defined member, and a resume-only code is not a stop reason either.
            var badNumber = await DeviceEndpoints.GetEmergencies(null, ["EmergencyStop"], ["999"], null, null,
                store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(badNumber);

            var wrongDirection = await DeviceEndpoints.GetEmergencies(null, ["EmergencyStop"],
                ["AlarmMonitorResumed"], null, null, store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(wrongDirection);
        }
    }

    [Fact]
    public async Task GetAlarms_Happy_ReturnsOkEnvelope()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddAlarmHistoryAsync(new ApiAlarmStatus { AlarmId = ApiAlarmKind.AnimalMissing, IsActive = true });

            var result = await DeviceEndpoints.GetAlarms(null, null, null, null, null, store, CancellationToken.None);

            var ok = Assert.IsType<Ok<PagedResult<AlarmDto>>>(result);
            Assert.Equal(1, ok.Value!.TotalCount);
            Assert.Equal(ApiAlarmKind.AnimalMissing, ok.Value.Items[0].AlarmId);
        }
    }
}
