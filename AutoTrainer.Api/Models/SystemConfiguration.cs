using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;

namespace AutoTrainer.Api.Models;

public class SystemConfiguration
{
    public string ApplicationVersion { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string ConfigurationLocation { get; set; } = "";

    public string DataLocation { get; set; } = "";

    public string AnimalFilesLocation { get; set; } = "";

    public string LogLocation { get; set; } = "";

    public string InferenceModel { get; set; } = "";

    // Service-owned: the newest entry in the device's system-note log, null when it is empty. No
    // ApiSystemConfiguration field carries it, so ApplyStatus must leave it alone or every arriving config would
    // wipe it off the retained instance.
    public NoteDto? SystemNote { get; set; }

    public void ApplyStatus(ApiSystemConfiguration config)
    {
        ApplicationVersion = config.ApplicationVersion;
        DeviceId = config.DeviceId;
        ConfigurationLocation = config.ConfigurationLocation;
        DataLocation = config.DataLocation;
        AnimalFilesLocation = config.AnimalLocation;
        LogLocation = config.LogLocation;
        InferenceModel = config.InferenceModel;
    }
}
