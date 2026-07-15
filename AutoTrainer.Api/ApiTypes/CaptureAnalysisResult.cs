namespace AutoTrainer.Api.ApiTypes;

// CaptureAnalysisResult values carried by TrialEnded (ApiTrialEndedPayload.Result).
public static class CaptureAnalysisResult
{
    public const string CaptureOnly = "capture_only";
    public const string AnalysisSucceeded = "analysis_succeeded";
    public const string AnalysisFailed = "analysis_failed";
    public const string AnalysisDelayed = "analysis_delayed";
}
