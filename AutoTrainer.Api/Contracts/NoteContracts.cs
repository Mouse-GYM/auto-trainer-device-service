namespace AutoTrainer.Api.Contracts;

// One note, in every context: the paged log, a mutation response, the live model, and the animal DTOs.
// WrittenAt/EditedAt are the row's CreatedAt/UpdatedAt -- renamed like AlarmDto.ObservedAt, so no raw audit
// column reaches the wire. EditedAt equals WrittenAt on a note that has never been edited. AuthorId is always
// null today; it is a reserved slot, not an accepted input.
public sealed record NoteDto(int Id, string Body, string? AuthorId, DateTime WrittenAt, DateTime EditedAt);

// Request body for a note create (POST) and edit (PATCH). Body is the only writable member; a missing or blank
// one is a 400, since deleting the note is how a note is removed.
public sealed record NoteWrite(string? Body);

// Which log a NoteChanged notice refers to. Numeric on the wire, like every other enum in the contracts.
public enum NoteScope { System, Behavior }

public enum NoteChangeKind { Created, Edited, Deleted }

// The mutation signal: identity only, so a client learns *when* it may be stale without every mutation
// shipping a log. AnimalIdentifier is null for Scope.System.
public sealed record NoteChangeDto(NoteScope Scope, string? AnimalIdentifier, int NoteId, NoteChangeKind Kind);
