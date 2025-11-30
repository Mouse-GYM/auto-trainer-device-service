using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.CommandQueue;

public class CommandQueueWorker : BackgroundService
{
    private RequestSocket? _requestSocket;

    private readonly HubConnection _hubConnection;

    private readonly ILogger<CommandQueueWorker> _logger;

    private readonly string _socketConnectionUrl;

    private ApiCommandRequest<object>? nextCommand = null;

    private const string _defaultSocketConnectionUrl = "tcp://127.0.0.1:5557";

    private readonly JsonSerializerOptions serializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int s_MaxRetries = 10;

    public CommandQueueWorker(ILogger<CommandQueueWorker> logger, IConfiguration configuration)
    {
        _logger = logger;

        _hubConnection = new HubConnectionBuilder().WithUrl("http://localhost:5150/messages").Build();
        _hubConnection.On<string, string>("SendCommand", PerformConnect);

        _socketConnectionUrl = configuration.GetSection(AutoTrainerOptions.AutoTrainer)?.GetSection(QueueOptions.CommandQueue)?.GetValue<string>(QueueOptions.ConnectionKey) ?? _defaultSocketConnectionUrl;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartHubConnectionAsync(stoppingToken);

        ConnectCommandSocket();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (nextCommand != null)
            {
                await SendNextCommandRequest(stoppingToken);
            }
            else
            {
                await Task.Delay(2000, stoppingToken);

                // performConnect();
            }
        }

        _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

        await StopHubConnectionAsync(stoppingToken);
    }

    private void PerformConnect(string user, string message)
    {
        _logger.LogInformation("Connect called");

        nextCommand = new ApiCommandRequest<object>(ApiCommandKind.UserDefined, new Dictionary<string, object>()
     {
            { "fooBar", 20 },
            { "barFoo", "hello" },
            { "data", 3.41},
        });
    }

    private async Task StartHubConnectionAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _hubConnection.StartAsync(cancellationToken);

                break;
            }
            catch
            {
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private async Task StopHubConnectionAsync(CancellationToken cancellationToken)
    {
        await _hubConnection.DisposeAsync();
    }

    private void ConnectCommandSocket()
    {
        var s = new RequestSocket();

        s.Connect(_socketConnectionUrl);

        _requestSocket = s;
    }

    private async Task SendNextCommandRequest(CancellationToken stoppingToken)
    {
        if (_requestSocket == null)
        {
            return;
        }

        if (nextCommand is ApiCommandRequest<object> request)
        {
            _logger.LogDebug("sending command request {cmd}", request.Command);

            if (_requestSocket.TrySendFrame(JsonSerializer.Serialize(request, serializeOptions)))
            {
                _logger.LogDebug("response to command request {cmd} is pending", request.Command);

                var response = await ReceiveCommandRequestResponse(stoppingToken);

                if (response != null)
                {
                    _logger.LogDebug("response to command request {cmd} received", request.Command);

                    nextCommand = null;
                }
            }
            else
            {
                _logger.LogWarning("failed to send frame {cmd}", request.Command);
            }
        }
    }

    private async Task<string?> ReceiveCommandRequestResponse(CancellationToken stoppingToken)
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

                var s = socket;

                _requestSocket = null;

                s.Close();
                s.Dispose();

                ConnectCommandSocket();
            }

            if (resp != null)
            {
                var str = Encoding.UTF8.GetString(resp);

                _logger.LogInformation("{resp}", str);

                return str;
            }
        }

        return null;
    }
}
