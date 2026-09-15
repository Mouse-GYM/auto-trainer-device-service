namespace AutoTrainer.Api.Data.Entities;

// An event that is not hand-based, from IntertrialResponse.OtherEvents. Its only relationship is the
// IntertrialResult it inherits; the trial is reached through that, and the session and batch through the trial.
public class OtherReachEvent : ReachEventBase
{
}
