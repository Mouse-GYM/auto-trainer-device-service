using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using Xunit;

namespace AutoTrainer.Api.Tests.ApiTypes;

// Guards the kind -> payload contract. The Python side gets this from mypy (build_event has exactly one
// overload per ApiEventKind); on this side the map is hand-maintained, so these tests are what stop it
// drifting from the enum when sync-api-types adds a kind.
public class ApiEventPayloadMapTests
{
    [Fact]
    public void EveryEventKind_HasAnEntry()
    {
        var missing = Enum.GetValues<ApiEventKind>()
            .Where(kind => !ApiEventPayloadMap.ForKind.ContainsKey(kind))
            .ToList();

        Assert.True(missing.Count == 0,
            "ApiEventKind members with no entry in ApiEventPayloadMap (add them, using null for a kind that "
            + $"carries no payload): {string.Join(", ", missing)}");
    }

    [Fact]
    public void MapHasNoEntriesForKindsThatDoNotExist()
    {
        var stale = ApiEventPayloadMap.ForKind.Keys
            .Where(kind => !Enum.IsDefined(kind))
            .ToList();

        Assert.True(stale.Count == 0, $"ApiEventPayloadMap entries for unknown kinds: {string.Join(", ", stale)}");
    }

    // The payload has to survive System.Text.Json round-tripping: every mapped type must be constructible
    // and settable by the deserializer (the wire form is camelCase JSON).
    [Fact]
    public void EveryPayloadType_IsDeserializable()
    {
        foreach (var type in ApiEventPayloadMap.AllPayloadTypes)
        {
            var instance = JsonSerializer.Deserialize("{}", type, JsonDefaults.CamelCase);
            Assert.NotNull(instance);
        }
    }

    [Theory]
    [InlineData(ApiEventKind.SessionStarted, typeof(ApiSessionStartedPayload))]
    [InlineData(ApiEventKind.SessionEnded, typeof(ApiSessionEndedPayload))]
    [InlineData(ApiEventKind.TrialStarted, typeof(ApiTrialStartedPayload))]
    [InlineData(ApiEventKind.TrialEnded, typeof(ApiTrialEndedPayload))]
    [InlineData(ApiEventKind.TrialCaptureEnded, typeof(ApiSessionTrialPayload))]
    [InlineData(ApiEventKind.TrialAnimalSeen, typeof(ApiTrialSeenPayload))]
    [InlineData(ApiEventKind.IntertrialDetectionBegin, typeof(ApiAnalysisTrialPayload))]
    [InlineData(ApiEventKind.IntertrialDetectionSave, typeof(ApiIntertrialSavePayload))]
    [InlineData(ApiEventKind.IntertrialDetectionError, typeof(ApiIntertrialErrorPayload))]
    [InlineData(ApiEventKind.IntertrialPelletShift, typeof(ApiPelletShiftPayload))]
    [InlineData(ApiEventKind.TrialReachEvents, typeof(ApiTrialReachEventsPayload))]
    [InlineData(ApiEventKind.BatchAnalysisStarted, typeof(ApiBatchAnalysisStartedPayload))]
    [InlineData(ApiEventKind.BatchAnalysisEnded, typeof(ApiBatchAnalysisEndedPayload))]
    // Status dataclasses are payloads too -- they are not *Context types.
    [InlineData(ApiEventKind.SystemStatus, typeof(ApiSystemStatus))]
    [InlineData(ApiEventKind.AlarmChanged, typeof(ApiAlarmStatus))]
    [InlineData(ApiEventKind.DetectorChanged, typeof(ApiDetectorStatus))]
    [InlineData(ApiEventKind.AnimalSelected, typeof(ApiAnimalStatus))]
    // The six protocol* events share one wire shape defined by the autotrainer.training package.
    [InlineData(ApiEventKind.ProtocolEvent, typeof(ApiProtocolEventPayload))]
    public void KnownKinds_MapToExpectedPayload(ApiEventKind kind, Type expected)
    {
        Assert.Equal(expected, ApiEventPayloadMap.PayloadType(kind));
    }

    // These carry no context at all; a consumer must not try to deserialize one.
    [Theory]
    [InlineData(ApiEventKind.TunnelEnter)]
    [InlineData(ApiEventKind.TunnelExit)]
    [InlineData(ApiEventKind.AlgorithmPause)]
    [InlineData(ApiEventKind.AlgorithmResume)]
    [InlineData(ApiEventKind.HeadfixAutoTare)]
    [InlineData(ApiEventKind.CalibrationDcsCompleted)]
    [InlineData(ApiEventKind.Calibration3dCompleted)]
    [InlineData(ApiEventKind.Unknown)]
    public void NoPayloadKinds_MapToNull(ApiEventKind kind)
    {
        Assert.True(ApiEventPayloadMap.ForKind.ContainsKey(kind));
        Assert.Null(ApiEventPayloadMap.PayloadType(kind));
    }

    [Fact]
    public void UnrecognizedKindOffTheWire_HasNoPayloadType()
    {
        Assert.Null(ApiEventPayloadMap.PayloadType((ApiEventKind)123456));
    }
}
