namespace AutoTrainer.Api.Contracts;

// Method/Outcome are the stored integer codes (see ReachEventMethod.ToCode / ReachEventOutcome.ToCode);
// callers filter and read them as codes, not strings. ObservedAt is the row's CreatedAt.
public sealed record ReachEventDto(int Id, DateTime ObservedAt, int TrialId,
    int Method, int Outcome, int FirstFrame, int? LastFrame, int? MaxFrame, double DelaySincePresented);
