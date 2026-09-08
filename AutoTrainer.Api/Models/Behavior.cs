using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public class Behavior
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double BaselineMagnetIntensity { get; set; }

    // Not part of status - is very transient.  Indicates load cell active in the context of the
    // behavior algorithm, not just raw sensor analysis.
    public bool LoadCellTriggered { get; set; }

    // Likewise transient and behavior-scoped: the head-fixation force detector as the algorithm sees it,
    // which is distinct from Analysis.HeadbarPressureEngaged (the raw sensor reading).
    public bool HeadbarPressureTriggered { get; set; }

    // Behavior settings, each set by the like-named ApiCommandKind in the 1000-1099 range. A setting keeps its
    // value while whatever gates it is off, so these report what is configured rather than what is in force.
    public bool IsLiveAnalysisEnabled { get; set; }

    public bool IsPelletDeliveryEnabled { get; set; }

    public bool IsPelletCoverEnabled { get; set; }

    public bool IsIntertrialPelletShiftEnabled { get; set; }

    public bool IsTrianglePelletDistanceDetectionEnabled { get; set; }

    public bool IsAutoCloseGateOnIntertrialEnabled { get; set; }

    // The only one of these settings the producer publishes an event for (autoClampEnabledChanged), so it is
    // also updated between status snapshots; the rest change silently and refresh on the next systemStatus.
    public bool IsAutoClampEnabled { get; set; }

    public bool IsBatchTrialsEnabled { get; set; }

    public void ApplyStatus(ApiBehaviorStatus status)
    {
        BaselineMagnetIntensity = status.BaselineMagnetIntensity;

        IsLiveAnalysisEnabled = status.IsLiveAnalysisEnabled;
        IsPelletDeliveryEnabled = status.IsPelletDeliveryEnabled;
        IsPelletCoverEnabled = status.IsPelletCoverEnabled;
        IsIntertrialPelletShiftEnabled = status.IsIntertrialPelletShiftEnabled;
        IsTrianglePelletDistanceDetectionEnabled = status.IsTrianglePelletDistanceDetectionEnabled;
        IsAutoCloseGateOnIntertrialEnabled = status.IsAutoCloseGateOnIntertrialEnabled;
        IsAutoClampEnabled = status.IsAutoClampEnabled;
        IsBatchTrialsEnabled = status.IsBatchTrialsEnabled;
    }
}
