using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Emergency;

public interface IEmergencyQueue
{
    ValueTask EnqueueAsync(ApiEvent apiEvent);

    ValueTask<ApiEvent> DequeueAsync(CancellationToken cancellationToken);
}

public class EmergencyTaskQueue : IEmergencyQueue
{
    private readonly Channel<ApiEvent> _channel;

    public EmergencyTaskQueue()
    {
        var options = new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };

        _channel = Channel.CreateBounded<ApiEvent>(options);
    }

    public async ValueTask EnqueueAsync(ApiEvent apiEvent) =>
        await _channel.Writer.WriteAsync(apiEvent);

    public async ValueTask<ApiEvent> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);
}
