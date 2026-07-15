using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;
using Xunit;

namespace AutoTrainer.Api.Tests.Data;

// The DTOs are the wire contract. They must drop EF audit columns and structurally avoid the Trial<->ReachEvent
// cycle (a DTO has no back-navigation).
public class DtoSerializationTests
{
    private static TrialDto SampleTrial(IReadOnlyList<ReachEventDto>? reaches) => new(
        Identifier: 7, BatchId: "batch-1", Reason: "r", Result: "analysis_succeeded",
        StartedAt: DateTime.UtcNow, PelletPresentedAt: null, PelletSeenAt: null, AnimalSeenAt: null,
        RightHandSeenAt: null, CaptureEndedAt: null, EndedAt: null,
        IntertrialSegmentationBeginAt: null, IntertrialSegmentationEndAt: null, IntertrialSegmentationError: null,
        IntertrialSegmentationSaveAt: null, IntertrialSegmentationSaveLocation: null,
        IntertrialSegmentationSaveError: null,
        IntertrialDetectionBeginAt: null, IntertrialDetectionEndAt: null, IntertrialDetectionError: null,
        IntertrialDetectionSaveAt: null, IntertrialDetectionSaveLocation: null, IntertrialDetectionSaveError: null,
        IntertrialPelletShift: null, ReachEventCount: reaches?.Count ?? 0, Reaches: reaches);

    [Fact]
    public void TrialDto_DropsAuditColumns_NoTrialCycle_ExposesChildren()
    {
        var reach = new ReachEventDto(1, DateTime.UtcNow, 42, 2, 5, 10, 11, 10, 0.5);
        var json = JsonSerializer.Serialize(SampleTrial([reach]), JsonDefaults.CamelCase);

        Assert.DoesNotContain("createdAt", json);
        Assert.DoesNotContain("updatedAt", json);
        Assert.DoesNotContain("deletedAt", json);
        Assert.DoesNotContain("\"trial\":", json);   // no back-navigation, so no cycle

        Assert.Contains("\"reaches\":", json);
        Assert.Contains("\"batchId\":", json);
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
}
