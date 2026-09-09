namespace AutoTrainer.Api.ApiTypes;

// The response Data of every 1000-1099 behavior setting: the state actually in effect after the attempt.
//
// Deliberately not ApiIsEnabledPayload, which carries "isEnabled" and belongs to the *events*
// (AutoClampEnabledChanged and friends). The command response spells the same idea "enabled", so one type
// cannot serve both.
public class ApiEnabledPayload
{
    public bool Enabled { get; set; }
}

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
