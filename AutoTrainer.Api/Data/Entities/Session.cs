namespace AutoTrainer.Api.Data.Entities;

// A tunnel session, keyed by the session_id carried on every session/trial/batch/intertrial event.
// Every column sourced from an event is nullable: the producer may exit before sending an "end", and we
// may start mid-session and never see the "start". Only Identifier is required — without it there is
// nothing to resolve the row by, so there is nothing to create.
public class Session : SoftDeleteEntity
{
    public string Identifier { get; set; } = "";

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool? IsAnalysisDeferred { get; set; }
    public int? CaptureTrialCount { get; set; }
    public int? AnalysisTrialCount { get; set; }
    public int? FailedTrialCount { get; set; }

    public ICollection<Trial> Trials { get; set; } = [];
    public ICollection<BatchAnalysis> BatchAnalyses { get; set; } = [];
}
