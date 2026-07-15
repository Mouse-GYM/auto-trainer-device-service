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
            var result = await DeviceEndpoints.GetAlarms("banana", null, null, null, store, CancellationToken.None);
            Assert.IsType<BadRequest<string>>(result);
        }
    }

    [Fact]
    public async Task GetAlarms_InvalidAlarmId_Returns400()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            var result = await DeviceEndpoints.GetAlarms(null, ["NotAKind"], null, null, store, CancellationToken.None);
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
    public async Task GetAlarms_Happy_ReturnsOkEnvelope()
    {
        var (factory, store) = TestDeviceStore.CreateMigrated();
        using (factory)
        {
            await store.AddAlarmHistoryAsync(new ApiAlarmStatus { AlarmId = ApiAlarmKind.AnimalMissing, IsActive = true });

            var result = await DeviceEndpoints.GetAlarms(null, null, null, null, store, CancellationToken.None);

            var ok = Assert.IsType<Ok<PagedResult<AlarmDto>>>(result);
            Assert.Equal(1, ok.Value!.TotalCount);
            Assert.Equal(ApiAlarmKind.AnimalMissing, ok.Value.Items[0].AlarmId);
        }
    }
}
