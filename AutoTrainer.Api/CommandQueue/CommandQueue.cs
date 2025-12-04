using AutoTrainer.Api.Options;
using AutoTrainer.Api.ApiTypes;


namespace AutoTrainer.Api.CommandQueue;

public interface ICommandTaskQueue
{
    ValueTask EnqueueAsync(ApiCommandRequest request);

    ValueTask<ApiCommandRequest> DequeueAsync(CancellationToken cancellationToken);
}

public class CommandTaskQueue : ICommandTaskQueue
{
    private readonly Channel<ApiCommandRequest> _channel;

    private readonly CommandQueueOptions _options;

    public CommandTaskQueue(IOptions<CommandQueueOptions> commandQueueOptions)
    {
        _options = commandQueueOptions.Value;

        var options = new BoundedChannelOptions(_options.ChannelDepth)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };

        _channel = Channel.CreateBounded<ApiCommandRequest>(options);
    }

    public async ValueTask EnqueueAsync(ApiCommandRequest workItem) =>
        await _channel.Writer.WriteAsync(workItem);

    public async ValueTask<ApiCommandRequest> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);
}
