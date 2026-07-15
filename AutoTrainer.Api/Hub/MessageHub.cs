using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Hub;

public class HubCommandRequest
{
    public ApiCommandKind Command { get; set; } = ApiCommandKind.None;

    public int CustomCommand { get; set; } = -1;

    public int Nonce { get; set; } = -1;

    public object? Data { get; set; } = null;
}

public interface IMessageHub
{
    Task Heartbeat(ApiHeartBeat heartbeat);

    Task ServiceHeartbeat(ApiHeartBeat heartbeat);

    Task EventReceived(ApiEvent apiEvent);

    Task RequestCommand(HubCommandRequest request);

    Task CommandRequested(HubCommandRequest request);

    Task CommandResponseReceived(object response);

    Task AlarmChanged(Alarm alarm);

    Task AlarmsChanged(List<Alarm> alarms);

    Task DetectorChanged(Detector detector);

    Task DetectorsChanged(List<Detector> detectors);

    Task ApplicationModeChanged(ApiApplicationMode mode);

    Task TrainingModeChanged(ApiTrainingMode mode);

    Task SystemStateChanged(SystemState state);

    Task PelletDeviceChanged(PelletDevice pelletDevice);

    Task TunnelDeviceChanged(TunnelDevice tunnelDevice);

    Task AnalysisChanged(Analysis analysis);

    Task BehaviorChanged(Behavior behavior);

    Task ReachEventsChanged(List<ReachEvent> reachEvents);

    Task AnimalChanged(Animal? animal);

    Task SessionEnded(SessionEnded session);

    Task SystemConfigurationChanged(ApiSystemConfiguration config);

    Task DeviceDataPath(string? path);

    Task TrialPath(string? path);

    Task WebImagesPath(string? path);

    Task LatestWebImage(string? path);
}

public class MessageHub : Hub<IMessageHub>
{
    public async Task RequestCommand(HubCommandRequest request, ICommandTaskQueue queue)
    {
        await queue.EnqueueAsync(new ApiCommandRequest(request.Command, request.CustomCommand, request.Nonce, null));

        await Clients.All.CommandRequested(request);
    }
}
