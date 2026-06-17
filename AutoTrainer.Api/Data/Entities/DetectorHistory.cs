using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Data.Entities;

public class DetectorHistory : SoftDeleteEntity
{
    public ApiDetectorKind DetectorId { get; set; }
    public bool IsActive { get; set; }
    public bool IsEnabled { get; set; }
}
