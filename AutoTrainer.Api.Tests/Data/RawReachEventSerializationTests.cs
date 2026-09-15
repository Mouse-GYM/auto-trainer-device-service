using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using Entities = AutoTrainer.Api.Data.Entities;
using Xunit;

namespace AutoTrainer.Api.Tests.Data;

// GET /raw-reaches returns these rows straight from the store, so the entity IS the wire contract.
public class RawReachEventSerializationTests
{
    [Fact]
    public void RawReachEvent_ExposesTrialId_ButNotTheTrialNavigation()
    {
        var reach = new Entities.RawReachEvent
        {
            Id = 1,
            TrialId = 42,
            Method = 2,
            Outcome = 5,
            FirstFrame = 10,
            DelaySincePresented = 1.5
        };

        var json = JsonSerializer.Serialize(reach, JsonDefaults.CamelCase);

        Assert.Contains("\"trialId\":42", json);
        Assert.DoesNotContain("\"trial\":", json);
    }

    // The navigation is a cycle (Trial -> RawReachEvents -> Trial). [JsonIgnore] is what keeps it from throwing.
    [Fact]
    public void RawReachEvent_WithTrialLoaded_StillSerializes()
    {
        var trial = new Entities.Trial { Id = 42, Identifier = 7 };
        var reach = new Entities.RawReachEvent { Id = 1, TrialId = 42, Trial = trial };
        trial.RawReachEvents.Add(reach);   // the back-reference that closes the cycle

        var json = JsonSerializer.Serialize(reach, JsonDefaults.CamelCase);

        Assert.DoesNotContain("\"trial\":", json);
    }

    // The second cycle the three reach tables introduce: IntertrialResult -> children -> IntertrialResult.
    // [JsonIgnore] on ReachEventBase.IntertrialResult is what keeps this from throwing.
    [Fact]
    public void IntertrialResult_WithChildrenLoaded_StillSerializes()
    {
        var result = new Entities.IntertrialResult { Id = 3, TrialId = 42 };
        result.RawReachEvents.Add(new Entities.RawReachEvent
        {
            Id = 1, TrialId = 42, IntertrialResultId = 3, IntertrialResult = result
        });
        result.HandReachEvents.Add(new Entities.HandReachEvent
        {
            Id = 2, IntertrialResultId = 3, IntertrialResult = result
        });
        result.OtherReachEvents.Add(new Entities.OtherReachEvent
        {
            Id = 3, IntertrialResultId = 3, IntertrialResult = result
        });

        var json = JsonSerializer.Serialize(result, JsonDefaults.CamelCase);

        Assert.Contains("\"intertrialResultId\":3", json);
        Assert.DoesNotContain("\"intertrialResult\":", json);
        Assert.DoesNotContain("\"trial\":", json);
    }
}
