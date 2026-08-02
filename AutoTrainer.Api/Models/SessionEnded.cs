using AutoTrainer.Api.Contracts;

namespace AutoTrainer.Api.Models;

// SignalR payload for a session ending — a made-up entity that exists only for this message (not persisted,
// not a REST resource). It pairs the currently-selected animal's identifier (null when none is selected), the
// session's scalar summary, and the current rolling-24h session count. SessionSummaryDto is reused for the
// session portion: it is an exact match already used by the REST API.
public sealed record SessionEnded(string? AnimalIdentifier, SessionSummaryDto Session, int SessionCount24h);
