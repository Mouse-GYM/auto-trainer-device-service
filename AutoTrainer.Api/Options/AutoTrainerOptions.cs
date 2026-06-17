namespace AutoTrainer.Api.Options
{
    public class AutoTrainerOptions
    {
        public const string AutoTrainer = "AutoTrainer";

        public MessageQueueOptions? MessageQueue { get; set; }

        public CommandQueueOptions? CommandQueue { get; set; }

        public DataOptions? Data { get; set; }
    }
}
