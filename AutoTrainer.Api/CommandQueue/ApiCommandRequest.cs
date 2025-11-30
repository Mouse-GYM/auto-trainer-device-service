namespace AutoTrainer.Api.CommandQueue;

public readonly record struct ApiCommandRequest<T>
{
    public ApiCommandKind Command { get; }

    public T Data { get; }

    public ApiCommandRequest(ApiCommandKind command, T data)
    {
        (Command, Data) = (command, data);
    }
}
