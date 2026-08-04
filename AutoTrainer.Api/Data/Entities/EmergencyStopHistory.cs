using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Data.Entities;

// One row per emergencyStop / emergencyResume event, append-only. The producer's stop and resume events
// are not 1:1 (a second stop can arrive with no resume between, and some resumes post no event at all),
// so this is an event log, not an episode table -- any pairing is a read-time concern.
public class EmergencyStopHistory : SoftDeleteEntity
{
    public ApiEventKind Kind { get; set; }

    // Producer event time (ApiEvent.When). CreatedAt remains the write time.
    public DateTime OccurredAt { get; set; }

    // ApiEvent.Index -- time.perf_counter_ns() on the producer, identical on the event- and emergency-topic
    // copies of the same event, so the notification path can correlate the two. Monotonic with an undefined
    // reference point, so it is a correlation key only, never an identifier and never unique.
    public long EventIndex { get; set; }

    // Exactly one is set, per Kind. The raw string is always kept, including when a reason converts to
    // Unknown, so a new producer string is never lost.
    public ApiEmergencyStopReason? StopReasonId { get; set; }
    public ApiEmergencyResumeReason? ResumeReasonId { get; set; }
    public string ReasonText { get; set; } = "";

    // JSON array of ApiAlarmKind ids from the stop payload; "[]" when the stop carried none, null on
    // resume rows (the resume payload has no such field). Deliberately not queryable -- see AlarmHistory
    // for the state of any individual alarm.
    public string? ActiveAlarms { get; set; }

    // Set when an operator notification was successfully published for this event; null when notifications
    // are not configured, the event never reached the notification path, or publishing failed.
    public DateTime? NotificationSentAt { get; set; }
}
