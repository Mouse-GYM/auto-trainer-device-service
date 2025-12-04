using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.CommandQueue;

public class CommandQueueWorker : BackgroundService
{
    private RequestSocket? _requestSocket;

    private readonly string _socketConnectionUrl;

    private readonly ICommandTaskQueue _taskQueue;

    private readonly IHubContext<MessageHub, IMessageHub> _hubContext;

    private readonly AutotrainerDevice _device;

    private readonly CommandQueueOptions _options;

    private readonly ILogger<CommandQueueWorker> _logger;

    private const int s_MaxRetries = 10;

    private static readonly JsonSerializerOptions messageSerializationOptions = JsonDefaults.CamelCase;

    public CommandQueueWorker(ICommandTaskQueue queue, IHubContext<MessageHub, IMessageHub> hubContext, AutotrainerDevice device, IOptions<CommandQueueOptions> options, ILogger<CommandQueueWorker> logger)
    {
        (_taskQueue, _hubContext, _device, _options, _logger) = (queue, hubContext, device, options.Value, logger);

        _logger.LogInformation("Command queue connection: {connection}", options.Value.Connection);

        _socketConnectionUrl = _options.Connection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConnectCommandSocket();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextCommand = await _taskQueue.DequeueAsync(stoppingToken);

                await SendCommandRequest(nextCommand, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command");
            }
        }

        _logger.LogInformation("Worker exiting at: {time}", DateTimeOffset.Now);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        DisconnectCommandSocket();
    }

    private void ConnectCommandSocket()
    {
        var s = new RequestSocket();

        s.Connect(_socketConnectionUrl);

        _requestSocket = s;
    }

    private void DisconnectCommandSocket()
    {
        var socket = Interlocked.Exchange(ref _requestSocket, null);

        if (socket is not null)
        {
            socket.Options.Linger = TimeSpan.Zero;
            socket.Close();
            socket.Dispose();
        }
    }

    private async Task SendCommandRequest(ApiCommandRequest request, CancellationToken stoppingToken)
    {
        if (_requestSocket == null)
        {
            return;
        }

        _logger.LogDebug("sending command request {cmd}", request.Command);

        if (_requestSocket.TrySendFrame(JsonSerializer.Serialize(request, messageSerializationOptions)))
        {
            _logger.LogDebug("response to command request {cmd} is pending", request.Command);

            var response = await ReceiveCommandRequestResponse(stoppingToken);

            if (response is ApiCommandRequestResponse serviceResponse)
            {
                _logger.LogDebug("response to command request {cmd} received with result {result}", request.Command, serviceResponse.Result);
            }
        }
        else
        {
            _logger.LogWarning("failed to send frame {cmd}", request.Command);
        }
    }

    private async Task<ApiCommandRequestResponse?> ReceiveCommandRequestResponse(CancellationToken stoppingToken)
    {
        if (_requestSocket is RequestSocket socket)
        {
            byte[]? resp;

            var retries = 0;

            while (!socket.TryReceiveFrameBytes(TimeSpan.FromSeconds(0.50), out resp) && retries < s_MaxRetries)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogDebug("\tresponse pending, retries {r}", retries);

                await Task.Delay(500, stoppingToken);

                retries++;
            }

            if (retries == s_MaxRetries)
            {
                _logger.LogWarning("did not receive response, redoing socket");

                DisconnectCommandSocket();
                ConnectCommandSocket();

                await Task.Delay(100, stoppingToken);
            }

            if (resp != null)
            {
                var response = JsonSerializer.Deserialize<ApiCommandRequestResponse>(resp, messageSerializationOptions);

                _device.OnCommandResponse(response);

                return response;
            }
        }

        return null;
    }
}
