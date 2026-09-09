namespace AutoTrainer.Api.Contracts;

// Registry row: id + identifier + name + timestamps (firstSeen = CreatedAt, lastUpdated = UpdatedAt), plus the
// newest behavior note (null when the log is empty). The registry key is carried as well as the identifier: a
// client may need the numeric id even though every route addresses animals by Identifier.
public sealed record AnimalDto(int Id, string Identifier, string Name, string TrainerNotes,
    NoteDto? BehaviorNote, DateTime FirstSeen, DateTime LastUpdated);

// Single-animal detail from the animal's OWN database (latest AnimalHistory + latest reach-status snapshots).
// ReachStatus5Day is the rolling total of the last 5 calendar days from the day table (see FiveDayReachStatus),
// mirroring the live Animal.ReachStatus5Day but computed here from stored rows.
public sealed record AnimalDetailDto(string Identifier, string Name,
    string TrainerNotes, NoteDto? BehaviorNote,
    double DcsSendX, double DcsSendY, double DcsSendZ, double? TargetYLimit,
    ReachStatusSnapshotDto? ReachStatusTotal, ReachStatusSnapshotDto? ReachStatusDay,
    ReachStatusSnapshotDto? ReachStatus5Day);

// Day is null for the total and 5-day (aggregate) snapshots, set for the single-day snapshot.
public sealed record ReachStatusSnapshotDto(int PelletsPresented, int PelletsConsumed,
    int Reaches, int SuccessfulReaches, DateOnly? Day);

// The current animal's latest reach-status rows for GET /animal/reachstatus: the most recent Total row and the
// most recent Day row for the current device day. Either is null in the edge cases — no known device day yet,
// no Day row for it yet, or (barely after creation) no Total row yet.
public sealed record AnimalReachStatusDto(ReachStatusSnapshotDto? Total, ReachStatusSnapshotDto? Day);
