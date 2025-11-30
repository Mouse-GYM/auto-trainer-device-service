namespace AutoTrainer.Api;


public enum ApiTopic
{
    Any = 0,
    Heartbeat = 1001,
    Emergency = 2001,
    Event = 3001,
    CommandResult = 4001
}
