using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AutoTrainer.Api.Tests.Models;

// The 1000-1099 behavior settings: the command-response path that converges the retained model, and the
// auto-clamp event path that is the only one of the ten to also arrive as an event.
public class BehaviorSettingTests
{
    private static (AutotrainerDevice device, Mock<IMessageHub> clients) Build()
    {
        var clients = new Mock<IMessageHub>();
        var hub = new Mock<IHubContext<MessageHub, IMessageHub>>();
        var hubClients = new Mock<IHubClients<IMessageHub>>();
        hubClients.Setup(c => c.All).Returns(clients.Object);
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        var device = new AutotrainerDevice(
            new Mock<ICommandTaskQueue>().Object, hub.Object, NullLogger<AutotrainerDevice>.Instance,
            new Mock<IDeviceDataStore>().Object, new Mock<IAnimalDataStore>().Object);

        return (device, clients);
    }

    // DeviceUpdateWorker isn't running in tests; execute the queued action(s) ourselves.
    private static async Task DrainAsync(AutotrainerDevice device)
    {
        while (device.UpdateReader.TryRead(out var action))
            await action();
    }

    private static ApiCommandRequestResponse SettingResponse(ApiCommandKind command, bool enabled,
        ApiCommandRequestResult result = ApiCommandRequestResult.Success) =>
        new(nonce: 1, command: command, result: result,
            data: new Dictionary<string, object> { ["enabled"] = enabled });

    // Every setting, the model it lands on, and how to read it back. The same table the production switch
    // must agree with — a setting wired to the wrong property shows up here as a cross-check failure.
    public enum Model { Behavior, PelletDevice }

    private static readonly (ApiCommandKind Command, Model Model, Func<AutotrainerDevice, bool> Read)[] Settings =
    [
        (ApiCommandKind.SetLiveAnalysisEnabled, Model.Behavior, d => d.Behavior.IsLiveAnalysisEnabled),
        (ApiCommandKind.SetPelletDeliveryEnabled, Model.Behavior, d => d.Behavior.IsPelletDeliveryEnabled),
        (ApiCommandKind.SetPelletCoverEnabled, Model.Behavior, d => d.Behavior.IsPelletCoverEnabled),
        (ApiCommandKind.SetIntertrialPelletShiftEnabled, Model.Behavior, d => d.Behavior.IsIntertrialPelletShiftEnabled),
        (ApiCommandKind.SetHomeOnExcessiveDriftEnabled, Model.PelletDevice, d => d.PelletDevice.IsHomeOnExcessiveDriftEnabled),
        (ApiCommandKind.SetTrianglePelletDistanceDetectionEnabled, Model.Behavior, d => d.Behavior.IsTrianglePelletDistanceDetectionEnabled),
        (ApiCommandKind.SetAutoCloseGateOnIntertrialEnabled, Model.Behavior, d => d.Behavior.IsAutoCloseGateOnIntertrialEnabled),
        (ApiCommandKind.SetAutoClampEnabled, Model.Behavior, d => d.Behavior.IsAutoClampEnabled),
        (ApiCommandKind.SetTunnelSweepEnabled, Model.PelletDevice, d => d.PelletDevice.IsTunnelSweepEnabled),
        (ApiCommandKind.SetBatchTrialsEnabled, Model.Behavior, d => d.Behavior.IsBatchTrialsEnabled)
    ];

    public static TheoryData<ApiCommandKind> EverySetting()
    {
        var data = new TheoryData<ApiCommandKind>();
        foreach (var (command, _, _) in Settings)
            data.Add(command);

        return data;
    }

    private static (Model Model, Func<AutotrainerDevice, bool> Read) Spec(ApiCommandKind command)
    {
        var (_, model, read) = Settings.Single(s => s.Command == command);
        return (model, read);
    }

    [Fact]
    public void TheTableCoversEveryCommandInTheSettingRange()
    {
        var declared = Enum.GetValues<ApiCommandKind>()
            .Where(k => (int)k is >= 1000 and < 1100)
            .OrderBy(k => (int)k);

        Assert.Equal(declared, Settings.Select(s => s.Command).OrderBy(k => (int)k));
    }

    [Theory]
    [MemberData(nameof(EverySetting))]
    public async Task SuccessfulResponse_SetsItsOwnProperty_AndBroadcastsItsModel(ApiCommandKind command)
    {
        var (device, clients) = Build();
        var (model, read) = Spec(command);

        device.OnCommandResponse(SettingResponse(command, true));
        await DrainAsync(device);

        Assert.True(read(device));

        // Exactly one setting moved: a mis-wired switch case would show up as a second true here.
        Assert.Equal([command], Settings.Where(s => s.Read(device)).Select(s => s.Command));

        clients.Verify(c => c.BehaviorChanged(device.Behavior),
            model == Model.Behavior ? Times.Once() : Times.Never());
        clients.Verify(c => c.PelletDeviceChanged(device.PelletDevice),
            model == Model.PelletDevice ? Times.Once() : Times.Never());
    }

    [Theory]
    [MemberData(nameof(EverySetting))]
    public async Task SuccessfulResponse_AppliesFalseAsWellAsTrue(ApiCommandKind command)
    {
        var (device, _) = Build();
        var (_, read) = Spec(command);

        device.OnCommandResponse(SettingResponse(command, true));
        await DrainAsync(device);
        Assert.True(read(device));

        device.OnCommandResponse(SettingResponse(command, false));
        await DrainAsync(device);

        // A false that is dropped rather than applied would leave this true -- the failure a "did it change?"
        // assertion alone would miss.
        Assert.False(read(device));
    }

    // The response carries the state actually in effect, so a producer that answers with the opposite of the
    // request is still the authority.
    [Fact]
    public async Task SuccessfulResponse_AppliesTheReportedValue_NotTheRequestedOne()
    {
        var (device, _) = Build();

        device.OnCommandResponse(SettingResponse(ApiCommandKind.SetLiveAnalysisEnabled, false));
        await DrainAsync(device);

        Assert.False(device.Behavior.IsLiveAnalysisEnabled);
    }

    [Theory]
    [InlineData(ApiCommandRequestResult.Failed)]
    [InlineData(ApiCommandRequestResult.Unavailable)]
    [InlineData(ApiCommandRequestResult.Unrecognized)]
    public async Task NonSuccessResponse_ChangesNothingAndBroadcastsNothing(ApiCommandRequestResult result)
    {
        var (device, clients) = Build();

        device.OnCommandResponse(SettingResponse(ApiCommandKind.SetAutoClampEnabled, true, result));
        await DrainAsync(device);

        Assert.False(device.Behavior.IsAutoClampEnabled);
        clients.Verify(c => c.BehaviorChanged(It.IsAny<Behavior>()), Times.Never);
    }

    [Fact]
    public async Task ResponseWithNoPayload_ChangesNothingAndBroadcastsNothing()
    {
        var (device, clients) = Build();

        device.OnCommandResponse(new ApiCommandRequestResponse(
            nonce: 1, command: ApiCommandKind.SetAutoClampEnabled,
            result: ApiCommandRequestResult.Success, data: null));
        await DrainAsync(device);

        Assert.False(device.Behavior.IsAutoClampEnabled);
        clients.Verify(c => c.BehaviorChanged(It.IsAny<Behavior>()), Times.Never);
    }

    // A command outside 1000-1099 must not be mistaken for a setting, whatever its payload looks like.
    [Fact]
    public async Task NonSettingCommand_IsNotTreatedAsASetting()
    {
        var (device, clients) = Build();

        device.OnCommandResponse(SettingResponse(ApiCommandKind.EmergencyStop, true));
        await DrainAsync(device);

        clients.Verify(c => c.BehaviorChanged(It.IsAny<Behavior>()), Times.Never);
        clients.Verify(c => c.PelletDeviceChanged(It.IsAny<PelletDevice>()), Times.Never);
    }

    // Auto-clamp is the only one of the ten the producer also publishes an event for, so it converges by two
    // independent routes; both must land on the same property.
    [Fact]
    public async Task AutoClampEvent_UpdatesTheSameProperty_AsItsCommandResponse()
    {
        var (device, clients) = Build();

        device.OnApiEvent(new ApiEvent
        {
            Kind = ApiEventKind.AutoClampEnabledChanged,
            When = 1_770_000_000,
            Context = JsonSerializer.SerializeToElement(
                new ApiIsEnabledPayload { IsEnabled = true }, JsonDefaults.CamelCase)
        });
        await DrainAsync(device);

        Assert.True(device.Behavior.IsAutoClampEnabled);
        clients.Verify(c => c.BehaviorChanged(device.Behavior), Times.Once);

        device.OnCommandResponse(SettingResponse(ApiCommandKind.SetAutoClampEnabled, false));
        await DrainAsync(device);

        Assert.False(device.Behavior.IsAutoClampEnabled);
    }
}
