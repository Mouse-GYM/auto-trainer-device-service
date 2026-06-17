using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.CommandQueue;

public partial class CommandQueueWorker : BackgroundService
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

        LogQueueConnection(options.Value.Connection);

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
                LogCommandError(ex);
            }
        }

        LogWorkerExiting(DateTimeOffset.Now);
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

        LogSendingCommand(request.Command);

        if (_requestSocket.TrySendFrame(JsonSerializer.Serialize(request, messageSerializationOptions)))
        {
            LogCommandPending(request.Command);

            var response = await ReceiveCommandRequestResponse(stoppingToken);

            if (response is ApiCommandRequestResponse serviceResponse)
            {
                LogCommandResult(request.Command, serviceResponse.Result);
            }
        }
        else
        {
            LogSendFrameFailed(request.Command);
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

                LogResponsePending(retries);

                await Task.Delay(500, stoppingToken);

                retries++;
            }

            if (retries == s_MaxRetries)
            {
                LogNoResponse();

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Command queue connection: {connection}")]
    private partial void LogQueueConnection(string connection);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing command")]
    private partial void LogCommandError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker exiting at: {time}")]
    private partial void LogWorkerExiting(DateTimeOffset time);

    [LoggerMessage(Level = LogLevel.Debug, Message = "sending command request {cmd}")]
    private partial void LogSendingCommand(ApiCommandKind cmd);

    [LoggerMessage(Level = LogLevel.Debug, Message = "response to command request {cmd} is pending")]
    private partial void LogCommandPending(ApiCommandKind cmd);

    [LoggerMessage(Level = LogLevel.Debug, Message = "response to command request {cmd} received with result {result}")]
    private partial void LogCommandResult(ApiCommandKind cmd, ApiCommandRequestResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "failed to send frame {cmd}")]
    private partial void LogSendFrameFailed(ApiCommandKind cmd);

    [LoggerMessage(Level = LogLevel.Debug, Message = "\tresponse pending, retries {r}")]
    private partial void LogResponsePending(int r);

    [LoggerMessage(Level = LogLevel.Warning, Message = "did not receive response, redoing socket")]
    private partial void LogNoResponse();
}
