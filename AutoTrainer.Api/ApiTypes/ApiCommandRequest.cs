namespace AutoTrainer.Api.ApiTypes;

public readonly record struct ApiCommandRequest
{
    public ApiCommandKind Command { get; init; }

    public int CustomCommand { get; init; } = -1;

    public int Nonce { get; init; } = -1;

    public IDictionary<string, object>? Data { get; init; } = null;

    public ApiCommandRequest(ApiCommandKind command, int customCommand = -1, int nonce = -1, IDictionary<string, object>? data = null)
    {
        (Command, CustomCommand, Nonce, Data) = (command, customCommand, nonce, data);
    }
}
