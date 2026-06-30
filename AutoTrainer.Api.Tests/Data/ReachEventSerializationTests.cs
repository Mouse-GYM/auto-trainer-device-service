using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using Entities = AutoTrainer.Api.Data.Entities;
using Xunit;

namespace AutoTrainer.Api.Tests.Data;

// GET /device/reaches returns these rows straight from the store, so the entity IS the wire contract.
public class ReachEventSerializationTests
{
    [Fact]
    public void ReachEvent_ExposesTrialId_ButNotTheTrialNavigation()
    {
        var reach = new Entities.ReachEvent
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

    // The navigation is a cycle (Trial -> ReachEvents -> Trial). [JsonIgnore] is what keeps it from throwing.
    [Fact]
    public void ReachEvent_WithTrialLoaded_StillSerializes()
    {
        var trial = new Entities.Trial { Id = 42, Identifier = 7 };
        var reach = new Entities.ReachEvent { Id = 1, TrialId = 42, Trial = trial };
        trial.ReachEvents.Add(reach);   // the back-reference that closes the cycle

        var json = JsonSerializer.Serialize(reach, JsonDefaults.CamelCase);

        Assert.DoesNotContain("\"trial\":", json);
    }
}
