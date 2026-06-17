namespace AutoTrainer.Api.Options;

public class DataOptions
{
    public const string Data = "Data";

    // NOTE: spelling intentionally matches the configuration key "SQLLiteLocation"
    // specified by the task. Config binding is case-insensitive but the property
    // name must match the key. Empty => use the current working directory.
    public string SQLLiteLocation { get; set; } = "";
}
