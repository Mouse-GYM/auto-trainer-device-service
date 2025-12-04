namespace AutoTrainer.Api.ApiTypes;

public enum ApiCommandRequestResult
{
    Unrecognized = 0,
    Success = 100,
    Pending = 200,
    PendingWithNotification = 201,
    Failed = 400,
    Exception = 500,
    Unavailable = 9999
}
