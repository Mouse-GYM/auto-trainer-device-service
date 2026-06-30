namespace AutoTrainer.Api.Data.Entities;

// A batch-analysis group, keyed by batch_id. A session may run more than one batch, so the identifier is
// the only way to tell the brackets apart and to resolve a batch_id arriving on an intertrial event.
public class BatchAnalysis : SoftDeleteEntity
{
    public string Identifier { get; set; } = "";

    public int SessionId { get; set; }
    public Session? Session { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    // Carried by both batchAnalysisStarted and batchAnalysisEnded; taken from Started so a batch that never
    // ends still records the count we saw, then overwritten by the (authoritative) Ended value.
    public int? AnalysisTrialCount { get; set; }
    public int? FailedTrialCount { get; set; }

    public ICollection<Trial> Trials { get; set; } = [];
}
