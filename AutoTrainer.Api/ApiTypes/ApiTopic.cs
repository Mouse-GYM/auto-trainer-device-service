namespace AutoTrainer.Api.ApiTypes;


public enum ApiTopic
{
    Any = 0,
    Heartbeat = 1001,
    Emergency = 2001,
    Event = 4001,
    PropertyChange=5001,
    CommandResult = 6001
}
