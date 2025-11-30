namespace AutoTrainer.Api.Options;

public class QueueOptions
{
    public const string MessageQueue = "MessageQueue";

    public const string CommandQueue = "CommandQueue";

    public const string ConnectionKey = "Connection";

    public string Connection { get; set; } = "";
}
