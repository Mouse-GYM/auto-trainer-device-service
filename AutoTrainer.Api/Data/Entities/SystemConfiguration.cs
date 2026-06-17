namespace AutoTrainer.Api.Data.Entities;

public class SystemConfiguration : SoftDeleteEntity
{
    public string ApplicationVersion { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string ConfigurationLocation { get; set; } = "";
    public string DataLocation { get; set; } = "";
    public string AnimalLocation { get; set; } = "";
    public string LogLocation { get; set; } = "";
    public string InferenceModel { get; set; } = "";
}
