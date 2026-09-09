using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Hub;

public class HubCommandRequest
{
    public ApiCommandKind Command { get; set; } = ApiCommandKind.None;

    public int CustomCommand { get; set; } = -1;

    public int Nonce { get; set; } = -1;

    // The command's request payload, passed through to the producer verbatim. Typed as a map rather than
    // object so it binds straight to ApiCommandRequest.Data: every command that takes one takes an object
    // ({enabled: bool} for the 1000-1099 settings, {mode: ...} for the mode setters), and a payload that is
    // not an object is a malformed request rather than something to forward.
    public Dictionary<string, object>? Data { get; set; } = null;
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

    Task SystemConfigurationChanged(SystemConfiguration config);

    // Identity only: which log, which animal, which note, what happened. Fires for every note mutation,
    // including one to an animal that is not selected -- which no live-model broadcast can reach.
    Task NoteChanged(NoteChangeDto change);

    Task DeviceDataPath(string? path);

    Task TrialPath(string? path);

    Task WebImagesPath(string? path);

    Task LatestWebImage(string? path);
}

public class MessageHub : Hub<IMessageHub>
{
    public async Task RequestCommand(HubCommandRequest request, ICommandTaskQueue queue)
    {
        // Data is forwarded, not dropped: the producer answers FAILED ("requires data") for any command whose
        // payload is absent, so a null here makes every setting and mode command unusable through this ingress.
        await queue.EnqueueAsync(
            new ApiCommandRequest(request.Command, request.CustomCommand, request.Nonce, request.Data));

        await Clients.All.CommandRequested(request);
    }
}
