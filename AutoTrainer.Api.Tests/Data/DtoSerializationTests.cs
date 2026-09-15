using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Models;
using Xunit;

namespace AutoTrainer.Api.Tests.Data;

// The DTOs are the wire contract. They must drop EF audit columns and structurally avoid the
// Trial<->RawReachEvent cycle (a DTO has no back-navigation).
public class DtoSerializationTests
{
    private static TrialDto SampleTrial(IReadOnlyList<ReachEventDto>? rightHandReaches = null) => new(
        Identifier: 7, BatchId: "batch-1", Reason: "r", Result: "analysis_succeeded",
        StartedAt: DateTime.UtcNow, PelletPresentedAt: null, PelletSeenAt: null, AnimalSeenAt: null,
        RightHandSeenAt: null, CaptureEndedAt: null, EndedAt: null,
        IntertrialSegmentationBeginAt: null, IntertrialSegmentationEndAt: null, IntertrialSegmentationError: null,
        IntertrialSegmentationSaveAt: null, IntertrialSegmentationSaveLocation: null,
        IntertrialSegmentationSaveError: null,
        IntertrialDetectionBeginAt: null, IntertrialDetectionEndAt: null, IntertrialDetectionError: null,
        IntertrialDetectionSaveAt: null, IntertrialDetectionSaveLocation: null, IntertrialDetectionSaveError: null,
        IntertrialPelletShift: null, RightHandReachEventCount: 2, RawReachEventCount: 3,
        HandReachEventCount: 4, OtherReachEventCount: 5,
        FoodConsumed: 4, SuccessfulReaches: 5, TotalReaches: 6, RhMaxVpList: "[[1,2,3]]",
        RightHandReaches: rightHandReaches);

    [Fact]
    public void TrialDto_DropsAuditColumns_NoTrialCycle_ExposesTheFourCounts()
    {
        var json = JsonSerializer.Serialize(SampleTrial(), JsonDefaults.CamelCase);

        Assert.DoesNotContain("createdAt", json);
        Assert.DoesNotContain("updatedAt", json);
        Assert.DoesNotContain("deletedAt", json);
        Assert.DoesNotContain("\"trial\":", json);           // no back-navigation, so no cycle
        Assert.DoesNotContain("intertrialResultId", json);   // an entity column, never on the wire

        Assert.Contains("\"rightHandReachEventCount\":2", json);
        Assert.Contains("\"rawReachEventCount\":3", json);
        Assert.Contains("\"handReachEventCount\":4", json);
        Assert.Contains("\"otherReachEventCount\":5", json);
        Assert.Contains("\"rhMaxVpList\":\"[[1,2,3]]\"", json);
        Assert.Contains("\"batchId\":", json);
    }

    // RightHandReaches is the trial's ONLY reach list; raw/hand/other rows come from the reach endpoints.
    [Fact]
    public void TrialDto_CarriesOnlyTheRightHandReachList()
    {
        var reach = new ReachEventDto(1, DateTime.UtcNow, 42, 2, 5, 10, 11, 10, 0.5);
        var json = JsonSerializer.Serialize(SampleTrial([reach]), JsonDefaults.CamelCase);

        Assert.Contains("\"rightHandReaches\":[{", json);
        Assert.DoesNotContain("\"rawReaches\":", json);
        Assert.DoesNotContain("\"handReaches\":", json);
        Assert.DoesNotContain("\"otherReaches\":", json);
    }

    // Null and [] mean different things here: null is "not expanded", [] is "expanded, nothing to return".
    [Fact]
    public void TrialDto_Unexpanded_SerializesRightHandReachesAsNull()
    {
        Assert.Contains("\"rightHandReaches\":null",
            JsonSerializer.Serialize(SampleTrial(), JsonDefaults.CamelCase));

        Assert.Contains("\"rightHandReaches\":[]",
            JsonSerializer.Serialize(SampleTrial([]), JsonDefaults.CamelCase));
    }

    [Fact]
    public void IntertrialResultDto_CarriesTheKeysScalarsAndAllThreeLists()
    {
        var dto = new IntertrialResultDto("session-1", 7, "batch-1", 4, 5, 6, "[[1,2,3]]",
            [new Api.ApiTypes.ReachEvent { Init = 1 }], [new Api.ApiTypes.ReachEvent { Init = 2 }], []);

        var json = JsonSerializer.Serialize(dto, JsonDefaults.CamelCase);

        Assert.Contains("\"sessionId\":\"session-1\"", json);
        Assert.Contains("\"trialId\":7", json);
        Assert.Contains("\"batchId\":\"batch-1\"", json);
        Assert.Contains("\"foodConsumed\":4", json);
        Assert.Contains("\"rhMaxVpList\":\"[[1,2,3]]\"", json);
        Assert.Contains("\"rawReachEvents\":[{", json);
        Assert.Contains("\"handReachEvents\":[{", json);
        Assert.Contains("\"otherReachEvents\":[]", json);
    }

    // WrittenAt/EditedAt are the row's CreatedAt/UpdatedAt renamed, so the audit columns must not reach the wire
    // under their own names.
    [Fact]
    public void NoteDto_ExposesWrittenAndEditedAt_DropsAuditColumns()
    {
        var json = JsonSerializer.Serialize(
            new NoteDto(1, "bench 3, left rack", null, DateTime.UtcNow, DateTime.UtcNow), JsonDefaults.CamelCase);

        Assert.Contains("\"writtenAt\":", json);
        Assert.Contains("\"editedAt\":", json);
        Assert.Contains("\"authorId\":null", json);
        Assert.DoesNotContain("createdAt", json);
        Assert.DoesNotContain("updatedAt", json);
        Assert.DoesNotContain("deletedAt", json);
    }

    // Clients switch on these, so the numeric encoding is part of the contract.
    [Fact]
    public void NoteChangeDto_SerializesEnumsAsNumbers()
    {
        var json = JsonSerializer.Serialize(
            new NoteChangeDto(NoteScope.Behavior, "mouse-1", 7, NoteChangeKind.Deleted), JsonDefaults.CamelCase);

        Assert.Contains("\"scope\":1", json);
        Assert.Contains("\"kind\":2", json);
        Assert.Contains("\"animalIdentifier\":\"mouse-1\"", json);
        Assert.Contains("\"noteId\":7", json);
    }

    [Fact]
    public void PagedResult_ExposesEnvelopeMetadata()
    {
        var alarm = new AlarmDto(1, DateTime.UtcNow, ApiAlarmKind.AnimalMissing, true, false, false, false);
        var json = JsonSerializer.Serialize(new PagedResult<AlarmDto>([alarm], 1, 50, 1), JsonDefaults.CamelCase);

        Assert.Contains("\"items\":", json);
        Assert.Contains("\"page\":", json);
        Assert.Contains("\"pageSize\":", json);
        Assert.Contains("\"totalCount\":", json);
        Assert.Contains("\"totalPages\":", json);
    }

    [Fact]
    public void ReachEventDto_MethodOutcome_SerializeAsNumbers_WithObservedAt()
    {
        var json = JsonSerializer.Serialize(new ReachEventDto(1, DateTime.UtcNow, 42, 2, 5, 10, null, null, 0.5),
            JsonDefaults.CamelCase);

        Assert.Contains("\"observedAt\":", json);
        Assert.Contains("\"method\":2", json);
        Assert.Contains("\"outcome\":5", json);
    }

    [Fact]
    public void AlarmDto_ExposesObservedAt()
    {
        var json = JsonSerializer.Serialize(
            new AlarmDto(1, DateTime.UtcNow, ApiAlarmKind.AnimalMissing, true, false, false, false),
            JsonDefaults.CamelCase);

        Assert.Contains("\"observedAt\":", json);
    }

    // The alarm set goes out as a real array of ids, not the JSON string the column stores. Kind and the
    // reason ids serialize as numbers, matching every other enum on the wire.
    [Fact]
    public void EmergencyDto_Stop_SerializesAlarmIdsAsArray()
    {
        var json = JsonSerializer.Serialize(
            new EmergencyDto(1, DateTime.UtcNow, ApiEventKind.EmergencyStop, ApiEmergencyStopReason.AlarmMonitor,
                null, "alarm-monitor: DOORS_OPEN", [ApiAlarmKind.ExternalDoors, ApiAlarmKind.SystemFault], null),
            JsonDefaults.CamelCase);

        Assert.Contains("\"occurredAt\":", json);
        Assert.Contains("\"kind\":101", json);
        Assert.Contains("\"stopReasonId\":101", json);
        Assert.Contains("\"resumeReasonId\":null", json);
        Assert.Contains("\"reasonText\":\"alarm-monitor: DOORS_OPEN\"", json);
        Assert.Contains("\"activeAlarms\":[101,501]", json);
        Assert.Contains("\"notificationSentAt\":null", json);

        Assert.DoesNotContain("createdAt", json);
        Assert.DoesNotContain("updatedAt", json);
        Assert.DoesNotContain("deletedAt", json);
        Assert.DoesNotContain("eventIndex", json);   // an internal correlation key, not part of the contract
    }

    // Null and [] mean different things: the resume payload has no alarm field at all.
    [Fact]
    public void EmergencyDto_Resume_SerializesNullActiveAlarms()
    {
        var json = JsonSerializer.Serialize(
            new EmergencyDto(2, DateTime.UtcNow, ApiEventKind.EmergencyResume, null,
                ApiEmergencyResumeReason.AlarmMonitorResume, "alarm-monitor-resumed", null, DateTime.UtcNow),
            JsonDefaults.CamelCase);

        Assert.Contains("\"activeAlarms\":null", json);
        Assert.Contains("\"stopReasonId\":null", json);
        Assert.Contains("\"resumeReasonId\":101", json);
    }
}
