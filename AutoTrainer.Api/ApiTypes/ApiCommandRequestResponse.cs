namespace AutoTrainer.Api.ApiTypes;

public readonly record struct ApiCommandRequestResponse
{
    public ApiCommandKind Command { get; init; }

    public int Nonce { get; init; }

    public ApiCommandRequestResult Result { get; init; }

    public IDictionary<string, object>? Data { get; init; }

    public ApiCommandRequestErrorKind ErrorKind { get; init; }

    public int ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public ApiCommandRequestResponse(
        int nonce,
        ApiCommandKind command,
        ApiCommandRequestResult result = ApiCommandRequestResult.Unrecognized,
        IDictionary<string, object>? data = null,
        ApiCommandRequestErrorKind errorKind = ApiCommandRequestErrorKind.None,
        int errorCode = 0,
        string? errorMessage = null)
    {
        (Nonce, Command, Result, Data, ErrorKind, ErrorCode, ErrorMessage) = (nonce, command, result, data, errorKind, errorCode, errorMessage);
    }
}
