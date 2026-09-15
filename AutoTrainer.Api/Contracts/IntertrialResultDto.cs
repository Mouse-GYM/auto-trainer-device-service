using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Contracts;

// The intertrialResponse event as broadcast on the hub (IntertrialResultChanged) and held on
// AutotrainerDevice.IntertrialResult. This is the pre-persistence shape taken straight off the payload, which
// is why the three lists carry the ApiTypes reach type rather than ReachEventDto -- these rows have no ids or
// timestamps yet. RhMaxVpList is the raw JSON string, matching how TrialDto returns it.
//
// The three lists are IntertrialResponse.ReachEvents/HandEvents/OtherEvents renamed to match the tables they
// land in -- the payload's own names never reach an interface we publish. All three are unfiltered, so a
// caller after the right-hand reaches alone filters HandReachEvents by method here.
public sealed record IntertrialResultDto(string SessionId, int TrialId, string? BatchId,
    int FoodConsumed, int SuccessfulReaches, int TotalReaches, string? RhMaxVpList,
    IReadOnlyList<ReachEvent> RawReachEvents, IReadOnlyList<ReachEvent> HandReachEvents,
    IReadOnlyList<ReachEvent> OtherReachEvents);
