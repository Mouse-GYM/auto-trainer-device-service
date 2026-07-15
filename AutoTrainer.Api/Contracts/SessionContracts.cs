namespace AutoTrainer.Api.Contracts;

public sealed record SessionSummaryDto(string Identifier, DateTime? StartedAt, DateTime? EndedAt,
    bool? IsAnalysisDeferred, int? CaptureTrialCount, int? AnalysisTrialCount, int? FailedTrialCount);

// Children are null unless requested via expand. TrialDto.Reaches is populated only when expand=trials.reaches.
public sealed record SessionDetailDto(string Identifier, DateTime? StartedAt, DateTime? EndedAt,
    bool? IsAnalysisDeferred, int? CaptureTrialCount, int? AnalysisTrialCount, int? FailedTrialCount,
    IReadOnlyList<TrialDto>? Trials, IReadOnlyList<BatchAnalysisDto>? Batches);

// Carries the full trial column set so one type serves both the trials list and the single-trial detail.
// BatchId is the batch's string Identifier (not the raw FK). Reaches is null in list contexts and populated
// only on the drill-down (trials/{trialId}?expand=reaches or sessions/{id}?expand=trials.reaches).
// IntertrialPelletShift is passed through as the raw JSON string, matching how it is stored.
public sealed record TrialDto(int Identifier, string? BatchId, string? Reason, string? Result,
    DateTime? StartedAt, DateTime? PelletPresentedAt, DateTime? PelletSeenAt, DateTime? AnimalSeenAt,
    DateTime? RightHandSeenAt, DateTime? CaptureEndedAt, DateTime? EndedAt,
    DateTime? IntertrialSegmentationBeginAt, DateTime? IntertrialSegmentationEndAt,
    string? IntertrialSegmentationError, DateTime? IntertrialSegmentationSaveAt,
    string? IntertrialSegmentationSaveLocation, string? IntertrialSegmentationSaveError,
    DateTime? IntertrialDetectionBeginAt, DateTime? IntertrialDetectionEndAt,
    string? IntertrialDetectionError, DateTime? IntertrialDetectionSaveAt,
    string? IntertrialDetectionSaveLocation, string? IntertrialDetectionSaveError,
    string? IntertrialPelletShift, int ReachEventCount, IReadOnlyList<ReachEventDto>? Reaches);

public sealed record BatchAnalysisDto(string Identifier, DateTime? StartedAt, DateTime? EndedAt,
    int? AnalysisTrialCount, int? FailedTrialCount);
