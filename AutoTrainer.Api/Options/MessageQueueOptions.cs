namespace AutoTrainer.Api.Options;

public class SnSOptions
{
    public string DeviceId { get; set; } = "";

    public string TopicArn { get; set; } = "";

    public string AccessKeyId { get; set; } = "";

    public string AccessKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TopicArn) && !string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(AccessKey);
}

public class MessageQueueOptions
{
    public const string MessageQueue = "MessageQueue";

    public string Connection { get; set; } = "";

    public SnSOptions SnS {get;set;} = new SnSOptions();
}
