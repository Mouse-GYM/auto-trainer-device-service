namespace AutoTrainer.Api.Data.Entities;

// A trial within a session. Identifier is the payload's trial_id, which is NOT unique across rows — it
// resets periodically — but is unique within a session, so (SessionId, Identifier) is the trial's key.
public class Trial : SoftDeleteEntity
{
    public int Identifier { get; set; }

    public int SessionId { get; set; }
    public Session? Session { get; set; }

    // Optional and populated late: batch ids do not exist until batch analysis starts, so in deferred mode a
    // trial's capture-phase events carry no batch_id even though it will eventually be analyzed in a batch.
    // Stays null in inline mode and when analysis is disabled.
    public int? BatchAnalysisId { get; set; }
    public BatchAnalysis? BatchAnalysis { get; set; }

    public string? Reason { get; set; }
    public string? Result { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? PelletPresentedAt { get; set; }
    public DateTime? PelletSeenAt { get; set; }
    public DateTime? AnimalSeenAt { get; set; }
    public DateTime? RightHandSeenAt { get; set; }
    public DateTime? CaptureEndedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public DateTime? IntertrialSegmentationBeginAt { get; set; }
    public DateTime? IntertrialSegmentationEndAt { get; set; }
    public string? IntertrialSegmentationError { get; set; }
    public DateTime? IntertrialSegmentationSaveAt { get; set; }
    public string? IntertrialSegmentationSaveLocation { get; set; }
    public string? IntertrialSegmentationSaveError { get; set; }

    public DateTime? IntertrialDetectionBeginAt { get; set; }
    public DateTime? IntertrialDetectionEndAt { get; set; }
    public string? IntertrialDetectionError { get; set; }
    public DateTime? IntertrialDetectionSaveAt { get; set; }
    public string? IntertrialDetectionSaveLocation { get; set; }
    public string? IntertrialDetectionSaveError { get; set; }

    // JSON of the intertrialPelletShift payload. Deliberately not queryable; deserialize to display it.
    public string? IntertrialPelletShift { get; set; }

    public ICollection<ReachEvent> ReachEvents { get; set; } = [];
}
