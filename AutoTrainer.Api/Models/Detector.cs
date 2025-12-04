using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public record Detector
{
    public ApiDetectorKind DetectorId { get; init; }
    public bool IsActive { get; init; }
    public bool IsEnabled { get; init; }
}
