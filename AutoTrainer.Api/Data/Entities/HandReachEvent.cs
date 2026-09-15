namespace AutoTrainer.Api.Data.Entities;

// A hand-based event that is not a reach, from IntertrialResponse.HandEvents. Its only relationship is the
// IntertrialResult it inherits; the trial is reached through that, and the session and batch through the trial.
public class HandReachEvent : ReachEventBase
{
}
