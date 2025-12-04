namespace AutoTrainer.Api.Options;

public class CommandQueueOptions
{
    public const string CommandQueue = "CommandQueue";

    public string Connection { get; set; } = "";

    public int ChannelDepth { get; set; } = 100;
}
