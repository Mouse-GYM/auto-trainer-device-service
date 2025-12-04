namespace AutoTrainer.Api.Options
{
    public class AutoTrainerOptions
    {
        public const string AutoTrainer = "AutoTrainer";

        public string DeviceId { get; set; } = "Device";

        public MessageQueueOptions? MessageQueue { get; set; }

        public CommandQueueOptions? CommandQueue { get; set; }
    }
}
