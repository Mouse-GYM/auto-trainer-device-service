using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Hub;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace AutoTrainer.Api.Tests.Hub;

// The hub's command ingress. Every command that takes a payload is unusable if Data does not survive this
// hop: the producer answers FAILED ("requires data") for an absent one.
public class MessageHubTests
{
    private static (MessageHub hub, Mock<ICommandTaskQueue> queue, List<ApiCommandRequest> queued) Build()
    {
        var queued = new List<ApiCommandRequest>();
        var queue = new Mock<ICommandTaskQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<ApiCommandRequest>()))
            .Callback<ApiCommandRequest>(queued.Add)
            .Returns(ValueTask.CompletedTask);

        var clients = new Mock<IHubCallerClients<IMessageHub>>();
        clients.Setup(c => c.All).Returns(new Mock<IMessageHub>().Object);

        return (new MessageHub { Clients = clients.Object }, queue, queued);
    }

    // What a real client sends: SignalR's JSON protocol binds an object member to JsonElement, so the queued
    // request must carry that through to the producer unchanged.
    private static Dictionary<string, object> JsonPayload(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonDefaults.CamelCase)!;

    [Fact]
    public async Task RequestCommand_ForwardsTheEnabledPayload()
    {
        var (hub, queue, queued) = Build();

        await hub.RequestCommand(new HubCommandRequest
        {
            Command = ApiCommandKind.SetAutoClampEnabled,
            Nonce = 7,
            Data = JsonPayload("""{"enabled":true}""")
        }, queue.Object);

        var request = Assert.Single(queued);
        Assert.Equal(ApiCommandKind.SetAutoClampEnabled, request.Command);
        Assert.Equal(7, request.Nonce);
        Assert.NotNull(request.Data);
        Assert.True(request.Data.ContainsKey("enabled"));

        // Asserted on the serialized form, because that is what actually reaches the producer.
        Assert.Contains("\"enabled\":true", JsonSerializer.Serialize(request, JsonDefaults.CamelCase));
    }

    [Fact]
    public async Task RequestCommand_ForwardsFalseRatherThanDroppingIt()
    {
        var (hub, queue, queued) = Build();

        await hub.RequestCommand(new HubCommandRequest
        {
            Command = ApiCommandKind.SetLiveAnalysisEnabled,
            Data = JsonPayload("""{"enabled":false}""")
        }, queue.Object);

        Assert.Contains("\"enabled\":false",
            JsonSerializer.Serialize(Assert.Single(queued), JsonDefaults.CamelCase));
    }

    // The mode setters take {mode: ...} rather than {enabled: bool}; nothing about the forwarding is
    // setting-specific, and this pins that.
    [Fact]
    public async Task RequestCommand_ForwardsAnyPayloadShape()
    {
        var (hub, queue, queued) = Build();

        await hub.RequestCommand(new HubCommandRequest
        {
            Command = ApiCommandKind.SetApplicationMode,
            Data = JsonPayload("""{"mode":"RUNNING"}""")
        }, queue.Object);

        Assert.Contains("\"mode\":\"RUNNING\"",
            JsonSerializer.Serialize(Assert.Single(queued), JsonDefaults.CamelCase));
    }

    // A command with no payload still queues one; null Data is correct here, and only here.
    [Fact]
    public async Task RequestCommand_WithNoPayload_QueuesNullData()
    {
        var (hub, queue, queued) = Build();

        await hub.RequestCommand(new HubCommandRequest { Command = ApiCommandKind.GetStatus }, queue.Object);

        Assert.Null(Assert.Single(queued).Data);
    }

    [Fact]
    public async Task RequestCommand_EchoesTheRequestToClients()
    {
        var queued = new List<ApiCommandRequest>();
        var queue = new Mock<ICommandTaskQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<ApiCommandRequest>()))
            .Callback<ApiCommandRequest>(queued.Add)
            .Returns(ValueTask.CompletedTask);

        var all = new Mock<IMessageHub>();
        var clients = new Mock<IHubCallerClients<IMessageHub>>();
        clients.Setup(c => c.All).Returns(all.Object);

        var hub = new MessageHub { Clients = clients.Object };
        var request = new HubCommandRequest
        {
            Command = ApiCommandKind.SetTunnelSweepEnabled,
            Data = JsonPayload("""{"enabled":true}""")
        };

        await hub.RequestCommand(request, queue.Object);

        all.Verify(c => c.CommandRequested(request), Times.Once);
    }
}
