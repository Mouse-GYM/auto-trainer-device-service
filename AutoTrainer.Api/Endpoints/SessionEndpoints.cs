using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/sessions").LogRequestsInDevelopment(app).LogClientDisconnects();

        sessions.MapGet("", GetSessions);
        sessions.MapGet("/count", GetSessionsCount);
        sessions.MapGet("/{sessionId}", GetSession);
        sessions.MapGet("/{sessionId}/trials", GetTrials);
        sessions.MapGet("/{sessionId}/trials/count", GetTrialsCount);
        sessions.MapGet("/{sessionId}/trials/{trialId:int}", GetTrial);
        sessions.MapGet("/{sessionId}/batches", GetBatches);

        return app;
    }

    internal static async Task<IResult> GetSessions(string? within, string? animal, bool? isAnalysisDeferred,
        int? page, int? pageSize, AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        // Unlike the device time-series endpoints, sessions default to NO time window: an unspecified `within`
        // returns every session for the animal (total = the animal's session count).
        DateTime? since = null;
        if (!string.IsNullOrWhiteSpace(within))
        {
            if (!TimeWindow.TryParse(within, out var window))
                return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");
            since = window.StartUtc(DateTime.UtcNow);
        }

        var pr = PageRequest.From(page, pageSize);

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(PagedResult<SessionSummaryDto>.Empty(pr.Page, pr.PageSize));

        var result = await store.GetSessionsAsync(res.Identifier!, since, isAnalysisDeferred, pr, ct);
        return TypedResults.Ok(result);
    }

    // Count analogue of GetSessions: same filters (within, animal, isAnalysisDeferred), no paging.
    internal static async Task<IResult> GetSessionsCount(string? within, string? animal, bool? isAnalysisDeferred,
        AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage, CancellationToken ct)
    {
        DateTime? since = null;
        if (!string.IsNullOrWhiteSpace(within))
        {
            if (!TimeWindow.TryParse(within, out var window))
                return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");
            since = window.StartUtc(DateTime.UtcNow);
        }

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(new CountDto(0));

        var count = await store.CountSessionsAsync(res.Identifier!, since, isAnalysisDeferred, ct);
        return TypedResults.Ok(new CountDto(count));
    }

    internal static async Task<IResult> GetSession(string sessionId, string? animal, string? expand, string? sort,
        AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage, CancellationToken ct)
    {
        if (!SessionExpand.TryParse(expand, out var expandFlags, out var expandErr))
            return TypedResults.BadRequest(expandErr);

        // `sort` orders the expanded trials (same convention/fields as GET /trials); it's inert without expand=trials.
        var sortReq = SortRequest.From(sort);
        if (!TrialSort.IsSupported(sortReq))
            return TypedResults.BadRequest($"Unsupported sort field '{sortReq.Field}'. Supported: {TrialSort.Identifier}.");

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.NotFound();   // single resource: no animal -> not found

        var detail = await store.GetSessionAsync(res.Identifier!, sessionId, expandFlags, sortReq, ct);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    internal static async Task<IResult> GetTrials(string sessionId, string? animal, bool? analysisPerformed,
        string? sort, int? page, int? pageSize, AutotrainerDevice device, IAnimalDataStore store,
        ISqliteStorage storage, CancellationToken ct)
    {
        var sortReq = SortRequest.From(sort);
        if (!TrialSort.IsSupported(sortReq))
            return TypedResults.BadRequest($"Unsupported sort field '{sortReq.Field}'. Supported: {TrialSort.Identifier}.");

        var pr = PageRequest.From(page, pageSize);

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(PagedResult<TrialDto>.Empty(pr.Page, pr.PageSize));

        var filter = new TrialFilter(analysisPerformed);
        var result = await store.GetTrialsAsync(res.Identifier!, sessionId, filter, sortReq, pr, ct);
        return TypedResults.Ok(result);
    }

    // Count analogue of GetTrials: same filters (animal, analysisPerformed), no sort/paging.
    internal static async Task<IResult> GetTrialsCount(string sessionId, string? animal, bool? analysisPerformed,
        AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage, CancellationToken ct)
    {
        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(new CountDto(0));

        var filter = new TrialFilter(analysisPerformed);
        var count = await store.CountTrialsAsync(res.Identifier!, sessionId, filter, ct);
        return TypedResults.Ok(new CountDto(count));
    }

    internal static async Task<IResult> GetTrial(string sessionId, int trialId, string? animal, string? expand,
        AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage, CancellationToken ct)
    {
        // The only recognized expand token here is `rightHandReaches`; every other reach list a trial could
        // have belongs to the reach endpoints.
        var expandRightHandReaches = false;
        if (!string.IsNullOrWhiteSpace(expand))
        {
            foreach (var token in expand.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(token, "rightHandReaches", StringComparison.OrdinalIgnoreCase))
                    expandRightHandReaches = true;
                else
                    return TypedResults.BadRequest($"Unknown expand token '{token}'. Valid: rightHandReaches.");
            }
        }

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.NotFound();

        var trial = await store.GetTrialAsync(res.Identifier!, sessionId, trialId, expandRightHandReaches, ct);
        return trial is null ? TypedResults.NotFound() : TypedResults.Ok(trial);
    }

    internal static async Task<IResult> GetBatches(string sessionId, string? animal, int? page, int? pageSize,
        AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage, CancellationToken ct)
    {
        var pr = PageRequest.From(page, pageSize);

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(PagedResult<BatchAnalysisDto>.Empty(pr.Page, pr.PageSize));

        var result = await store.GetBatchesAsync(res.Identifier!, sessionId, pr, ct);
        return TypedResults.Ok(result);
    }
}
