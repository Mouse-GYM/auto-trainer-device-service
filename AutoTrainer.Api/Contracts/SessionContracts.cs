namespace AutoTrainer.Api.Contracts;

public sealed record SessionSummaryDto(string Identifier, DateTime? StartedAt, DateTime? EndedAt,
    bool? IsAnalysisDeferred, int? CaptureTrialCount, int? AnalysisTrialCount, int? FailedTrialCount);

// Children are null unless requested via expand.
public sealed record SessionDetailDto(string Identifier, DateTime? StartedAt, DateTime? EndedAt,
    bool? IsAnalysisDeferred, int? CaptureTrialCount, int? AnalysisTrialCount, int? FailedTrialCount,
    IReadOnlyList<TrialDto>? Trials, IReadOnlyList<BatchAnalysisDto>? Batches);

// Carries the full trial column set so one type serves both the trials list and the single-trial detail.
// BatchId is the batch's string Identifier (not the raw FK).
//
// RightHandReachEventCount is the count that answers "how many reaches did this trial have": hand events whose
// method is right_hand, a subset of HandReachEventCount. All four counts are always present and are zero for a
// trial that was never analysed; the intertrial scalars below are null in that case.
//
// RightHandReaches is those same rows, and the trial's only reach list -- null means "not expanded", and it is
// non-null (empty rather than null when there is nothing to return, including for a trial with no
// IntertrialResult at all) on the expand paths, trials/{trialId}?expand=rightHandReaches and
// sessions/{id}?expand=trials.rightHandReaches. Raw and non-right-hand rows are not reachable from a trial;
// they come from /raw-reaches, /hand-reaches and /other-reaches.
//
// IntertrialPelletShift and RhMaxVpList are passed through as the raw JSON strings, matching how they are stored.
public sealed record TrialDto(int Identifier, string? BatchId, string? Reason, string? Result,
    DateTime? StartedAt, DateTime? PelletPresentedAt, DateTime? PelletSeenAt, DateTime? AnimalSeenAt,
    DateTime? RightHandSeenAt, DateTime? CaptureEndedAt, DateTime? EndedAt,
    DateTime? IntertrialSegmentationBeginAt, DateTime? IntertrialSegmentationEndAt,
    string? IntertrialSegmentationError, DateTime? IntertrialSegmentationSaveAt,
    string? IntertrialSegmentationSaveLocation, string? IntertrialSegmentationSaveError,
    DateTime? IntertrialDetectionBeginAt, DateTime? IntertrialDetectionEndAt,
    string? IntertrialDetectionError, DateTime? IntertrialDetectionSaveAt,
    string? IntertrialDetectionSaveLocation, string? IntertrialDetectionSaveError,
    string? IntertrialPelletShift, int RightHandReachEventCount, int RawReachEventCount,
    int HandReachEventCount, int OtherReachEventCount,
    int? FoodConsumed, int? SuccessfulReaches, int? TotalReaches, string? RhMaxVpList,
    IReadOnlyList<ReachEventDto>? RightHandReaches);

public sealed record BatchAnalysisDto(string Identifier, DateTime? StartedAt, DateTime? EndedAt,
    int? AnalysisTrialCount, int? FailedTrialCount);
