namespace AutoTrainer.Api.Options
{
    public class AutoTrainerOptions
    {
        public const string AutoTrainer = "AutoTrainer";

        public QueueOptions? MessageQueue { get; set; }

        public QueueOptions? CommandQueue { get; set; }
    }
}
