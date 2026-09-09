using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AutoTrainer.Api.Tests.Models;

public class AutotrainerDeviceNotesTests
{
    private static (AutotrainerDevice device, Mock<ICommandTaskQueue> queue, Mock<IMessageHub> clients,
        Mock<IDeviceDataStore> deviceStore, Mock<IAnimalDataStore> animalStore) Build()
    {
        var queue = new Mock<ICommandTaskQueue>();
        var clients = new Mock<IMessageHub>();
        var hub = new Mock<IHubContext<MessageHub, IMessageHub>>();
        var hubClients = new Mock<IHubClients<IMessageHub>>();
        hubClients.Setup(c => c.All).Returns(clients.Object);
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        var deviceStore = new Mock<IDeviceDataStore>();
        var animalStore = new Mock<IAnimalDataStore>();

        // Every animal selection refreshes the notes cache; an unstubbed Task<AnimalNotes> completes with null.
        // Each notes assertion below overrides this with its own value.
        deviceStore.Setup(s => s.GetAnimalNotesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnimalNotes.Empty);

        // Same for the device note, which OnSystemNoteChanged re-reads rather than being handed.
        deviceStore.Setup(s => s.GetLatestSystemNoteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteDto?)null);

        var device = new AutotrainerDevice(
            queue.Object, hub.Object, NullLogger<AutotrainerDevice>.Instance,
            deviceStore.Object, animalStore.Object);

        return (device, queue, clients, deviceStore, animalStore);
    }

    private static async Task DrainAsync(AutotrainerDevice device)
    {
        // DeviceUpdateWorker isn't running in tests; execute the queued action(s) ourselves.
        while (device.UpdateReader.TryRead(out var action))
        {
            await action();
        }
    }

    // Fixed timestamps so two notes built from the same (id, body) compare equal -- the "did the newest note
    // change?" branch under test is record value equality.
    private static readonly DateTime s_Written = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static NoteDto Note(int id, string body) => new(id, body, null, s_Written, s_Written);

    private static ApiEvent Event(ApiEventKind kind, object? payload) => new()
    {
        Kind = kind,
        When = 1_770_000_000,
        Context = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonDefaults.CamelCase)
    };

    private static async Task SelectAnimalAsync(AutotrainerDevice device, string identifier = "mouse-1")
    {
        device.OnApiEvent(Event(ApiEventKind.AnimalSelected, new ApiAnimalStatus { Identifier = identifier }));
        await DrainAsync(device);
    }

    private static void StubNotes(Mock<IDeviceDataStore> deviceStore, string identifier,
        string trainer, NoteDto? behavior) =>
        deviceStore.Setup(s => s.GetAnimalNotesAsync(identifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnimalNotes(trainer, behavior));

    private static void StubSystemNote(Mock<IDeviceDataStore> deviceStore, NoteDto? note) =>
        deviceStore.Setup(s => s.GetLatestSystemNoteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(note);

    // Captures every NoteChanged the device sends, so a test can assert on the payload rather than merely that
    // something was queued.
    private static List<NoteChangeDto> CaptureNoteChanges(Mock<IMessageHub> clients)
    {
        var seen = new List<NoteChangeDto>();
        clients.Setup(c => c.NoteChanged(It.IsAny<NoteChangeDto>()))
            .Callback<NoteChangeDto>(seen.Add)
            .Returns(Task.CompletedTask);
        return seen;
    }

    private static ApiCommandRequestResponse ConfigurationResponse(ApiSystemConfiguration config)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(
            JsonSerializer.Serialize(config, JsonDefaults.CamelCase), JsonDefaults.CamelCase);

        return new ApiCommandRequestResponse(
            nonce: 1, command: ApiCommandKind.GetConfiguration,
            result: ApiCommandRequestResult.Success, data: data);
    }

    [Fact]
    public async Task SystemConfigurationChanged_BroadcastsRetainedModel_WithNote()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(1, "bench 3, left rack"));

        // Snapshotted inside the callback rather than asserted off a captured reference. Every broadcast passes
        // the same retained Configuration instance, so a held reference would show ApplyStatus's later mutations
        // and these assertions would still pass with the second broadcast deleted outright.
        var sends = new List<(bool IsRetainedModel, NoteDto? SystemNote, string AnimalFilesLocation, string DeviceId)>();
        clients.Setup(c => c.SystemConfigurationChanged(It.IsAny<SystemConfiguration>()))
            .Callback<SystemConfiguration>(c => sends.Add(
                (ReferenceEquals(c, device.Configuration), c.SystemNote, c.AnimalFilesLocation, c.DeviceId)))
            .Returns(Task.CompletedTask);

        device.OnSystemNoteChanged(1, NoteChangeKind.Created);
        await DrainAsync(device);
        sends.Clear();   // drop the seeding broadcast; only the configuration response is under test

        device.OnCommandResponse(ConfigurationResponse(new ApiSystemConfiguration
        {
            DeviceId = "device-7",
            AnimalLocation = "/animals",
            DataLocation = "/data"
        }));
        await DrainAsync(device);

        // The retained model, not the api object: it carries the note and the model's own AnimalFilesLocation
        // spelling for the api object's AnimalLocation.
        var send = Assert.Single(sends);
        Assert.True(send.IsRetainedModel);
        Assert.Equal("bench 3, left rack", send.SystemNote!.Body);
        Assert.Equal("/animals", send.AnimalFilesLocation);
        Assert.Equal("device-7", send.DeviceId);
    }

    [Fact]
    public void SystemConfiguration_ApplyStatus_DoesNotClearNote()
    {
        var config = new SystemConfiguration { SystemNote = Note(1, "keep me") };

        config.ApplyStatus(new ApiSystemConfiguration
        {
            DeviceId = "device-7",
            AnimalLocation = "/animals",
            InferenceModel = "model-x"
        });

        Assert.Equal("keep me", config.SystemNote!.Body);
        Assert.Equal("device-7", config.DeviceId);
    }

    [Fact]
    public async Task SystemNoteChanged_BroadcastsWithNoApiMessage()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(4, "x"));

        device.OnSystemNoteChanged(4, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Equal("x", device.Configuration.SystemNote!.Body);
        clients.Verify(c => c.SystemConfigurationChanged(
            It.Is<SystemConfiguration>(cfg => cfg.SystemNote!.Body == "x")), Times.Once);
        deviceStore.Verify(s => s.AddSystemConfigurationAsync(
            It.IsAny<ApiSystemConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SystemNoteChanged_AlwaysNotifies_WithScopeIdAndKind()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(9, "current"));
        var changes = CaptureNoteChanges(clients);

        device.OnSystemNoteChanged(9, NoteChangeKind.Created);
        device.OnSystemNoteChanged(3, NoteChangeKind.Edited);
        device.OnSystemNoteChanged(2, NoteChangeKind.Deleted);
        await DrainAsync(device);

        Assert.Collection(changes,
            c => Assert.Equal(new NoteChangeDto(NoteScope.System, null, 9, NoteChangeKind.Created), c),
            c => Assert.Equal(new NoteChangeDto(NoteScope.System, null, 3, NoteChangeKind.Edited), c),
            c => Assert.Equal(new NoteChangeDto(NoteScope.System, null, 2, NoteChangeKind.Deleted), c));
    }

    // The assertion that pins the whole design: SystemConfigurationChanged exists to carry live-model values, and
    // the note that was edited need not be the newest one.
    [Fact]
    public async Task SystemNoteChanged_OlderNote_NotifiesButDoesNotRebroadcastConfiguration()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(9, "newest"));

        device.OnSystemNoteChanged(9, NoteChangeKind.Created);
        await DrainAsync(device);
        clients.Invocations.Clear();

        var changes = CaptureNoteChanges(clients);

        // An edit to note 3 leaves note 9 the newest, so the retained model has not changed.
        device.OnSystemNoteChanged(3, NoteChangeKind.Edited);
        await DrainAsync(device);

        Assert.Single(changes);
        clients.Verify(c => c.SystemConfigurationChanged(It.IsAny<SystemConfiguration>()), Times.Never);
    }

    // The hook reads the store instead of taking a value, so the durable write wins no matter which order two
    // overlapping requests reach the channel in. Two enqueues, one committed value.
    [Fact]
    public async Task SystemNoteChanged_ReadsDurableValue_NotACapturedOne()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(2, "b"));

        device.OnSystemNoteChanged(1, NoteChangeKind.Created);
        device.OnSystemNoteChanged(2, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Equal("b", device.Configuration.SystemNote!.Body);
    }

    [Fact]
    public async Task SystemNoteChanged_StoreThrows_NotifiesButKeepsPreviousValue()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(1, "seeded"));

        device.OnSystemNoteChanged(1, NoteChangeKind.Created);
        await DrainAsync(device);
        clients.Invocations.Clear();

        var changes = CaptureNoteChanges(clients);
        deviceStore.Setup(s => s.GetLatestSystemNoteAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        device.OnSystemNoteChanged(2, NoteChangeKind.Created);
        await DrainAsync(device);

        // The mutation did commit, so the notice stands; only the live-model broadcast is withheld.
        Assert.Single(changes);
        Assert.Equal("seeded", device.Configuration.SystemNote!.Body);
        clients.Verify(c => c.SystemConfigurationChanged(It.IsAny<SystemConfiguration>()), Times.Never);
    }

    // The NoteChanged send is awaited inside the queued action, and DeviceUpdateWorker catches per action -- so
    // a faulted send must not take the store re-read and the live-model update down with it.
    [Fact]
    public async Task SystemNoteChanged_NotifyThrows_StillRereadsAndBroadcasts()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(1, "x"));
        clients.Setup(c => c.NoteChanged(It.IsAny<NoteChangeDto>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        device.OnSystemNoteChanged(1, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Equal("x", device.Configuration.SystemNote!.Body);
        clients.Verify(c => c.SystemConfigurationChanged(It.IsAny<SystemConfiguration>()), Times.Once);
    }

    [Fact]
    public async Task GetConfiguration_PersistsApiConfig_WithoutNote()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(1, "not for the history row"));

        device.OnSystemNoteChanged(1, NoteChangeKind.Created);
        await DrainAsync(device);

        device.OnCommandResponse(ConfigurationResponse(new ApiSystemConfiguration { DeviceId = "device-7" }));
        await DrainAsync(device);

        // The history row records exactly what upstream sent, so it still takes the raw api object.
        deviceStore.Verify(s => s.AddSystemConfigurationAsync(
            It.Is<ApiSystemConfiguration>(c => c.DeviceId == "device-7"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetConfiguration_BroadcastThrows_StillPersistsAndRequestsStatus()
    {
        var (device, queue, clients, deviceStore, _) = Build();
        clients.Setup(c => c.SystemConfigurationChanged(It.IsAny<SystemConfiguration>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        device.OnCommandResponse(ConfigurationResponse(new ApiSystemConfiguration { DeviceId = "device-7" }));
        await DrainAsync(device);

        // The broadcast is awaited, so a faulted send must not take the rest of the command response with it.
        deviceStore.Verify(s => s.AddSystemConfigurationAsync(
            It.IsAny<ApiSystemConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.EnqueueAsync(
            It.Is<ApiCommandRequest>(r => r.Command == ApiCommandKind.GetStatus)), Times.Once);

        // The model still took the configuration, even though nobody heard about it.
        Assert.Equal("device-7", device.Configuration.DeviceId);
    }

    [Fact]
    public async Task Initialize_SeedsSystemNoteFromStore()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubSystemNote(deviceStore, Note(5, "from db"));

        await device.InitializeAsync();

        Assert.Equal("from db", device.Configuration.SystemNote!.Body);
        clients.Verify(c => c.SystemConfigurationChanged(It.IsAny<SystemConfiguration>()), Times.Never);
        clients.Verify(c => c.NoteChanged(It.IsAny<NoteChangeDto>()), Times.Never);
    }

    [Fact]
    public async Task Initialize_StoreThrows_LeavesNoteNull()
    {
        var (device, _, _, deviceStore, _) = Build();
        deviceStore.Setup(s => s.GetLatestSystemNoteAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await device.InitializeAsync();

        Assert.Null(device.Configuration.SystemNote);
    }

    // The direct form of the invariant. AnimalUpdated_DoesNotClearNotes below exercises the same property
    // end to end, but StampAndBroadcastAnimal re-stamps both notes right after ApplyStatus runs, so it stays
    // green even if ApplyStatus clears them -- only this test actually pins ApplyStatus.
    [Fact]
    public void Animal_ApplyStatus_DoesNotClearNotes()
    {
        var animal = new Animal { TrainerNotes = "keep t", BehaviorNote = Note(1, "keep b") };

        animal.ApplyStatus(new ApiAnimalStatus
        {
            Identifier = "mouse-1",
            Name = "Whiskers",
            DcsSendX = 1.0
        });

        Assert.Equal("keep t", animal.TrainerNotes);
        Assert.Equal("keep b", animal.BehaviorNote!.Body);
        Assert.Equal("Whiskers", animal.Name);
        Assert.Equal(1.0, animal.DcsSendX);
    }

    [Fact]
    public async Task AnimalSelected_StampsNotesFromRegistry()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t", Note(1, "b"));

        await SelectAnimalAsync(device);

        Assert.Equal("t", device.Animal!.TrainerNotes);
        Assert.Equal("b", device.Animal.BehaviorNote!.Body);
    }

    [Fact]
    public async Task AnimalUpdated_DoesNotClearNotes()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t", Note(1, "b"));

        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.AnimalUpdated,
            new ApiAnimalStatus { Identifier = "mouse-1", Name = "Renamed" }));
        await DrainAsync(device);

        // ApplyStatus runs on the retained instance, so touching the notes there would clear them here.
        Assert.Equal("Renamed", device.Animal!.Name);
        Assert.Equal("t", device.Animal.TrainerNotes);
        Assert.Equal("b", device.Animal.BehaviorNote!.Body);
    }

    [Fact]
    public async Task CountChanged_KeepsNotesStamped()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t", Note(1, "b"));

        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.DayReachCountChanged, new ApiCountChangePayload { Count = 7 }));
        await DrainAsync(device);

        Assert.Equal("t", device.Animal!.TrainerNotes);
        Assert.Equal("b", device.Animal.BehaviorNote!.Body);

        // The cache holds: a count change stamps from it rather than re-reading on this hot path.
        deviceStore.Verify(s => s.GetAnimalNotesAsync("mouse-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SelectingDifferentAnimal_ReadsNotesAgain()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));
        StubNotes(deviceStore, "mouse-2", "t2", Note(2, "b2"));

        await SelectAnimalAsync(device, "mouse-1");
        await SelectAnimalAsync(device, "mouse-2");

        Assert.Equal("t2", device.Animal!.TrainerNotes);
        Assert.Equal("b2", device.Animal.BehaviorNote!.Body);
        deviceStore.Verify(s => s.GetAnimalNotesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // A *selection change* whose read fails must still reset to empty -- the opposite of the forced same-animal
    // refresh below. The two failure semantics are deliberately different, so both need pinning.
    [Fact]
    public async Task NotesReadFails_ResetsToEmpty_NotPreviousAnimal()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));
        deviceStore.Setup(s => s.GetAnimalNotesAsync("mouse-2", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await SelectAnimalAsync(device, "mouse-1");
        await SelectAnimalAsync(device, "mouse-2");

        Assert.Equal("mouse-2", device.Animal!.Identifier);
        Assert.Equal("", device.Animal.TrainerNotes);
        Assert.Null(device.Animal.BehaviorNote);
    }

    [Fact]
    public async Task UnregisteredAnimal_NotesAreEmpty()
    {
        var (device, _, _, deviceStore, _) = Build();
        deviceStore.Setup(s => s.GetAnimalNotesAsync("mouse-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnimalNotes.Empty);

        await SelectAnimalAsync(device);

        Assert.Equal("", device.Animal!.TrainerNotes);
        Assert.Null(device.Animal.BehaviorNote);
    }

    [Fact]
    public async Task AnimalSelected_WithNullPayload_ClearsNotesCache()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));

        await SelectAnimalAsync(device);

        device.OnApiEvent(Event(ApiEventKind.AnimalSelected, null));
        await DrainAsync(device);
        Assert.Null(device.Animal);

        StubNotes(deviceStore, "mouse-1", "t2", Note(2, "b2"));
        await SelectAnimalAsync(device);

        // Re-read rather than served from the cleared cache, which still remembered mouse-1 before the clear.
        Assert.Equal("t2", device.Animal!.TrainerNotes);
        Assert.Equal("b2", device.Animal.BehaviorNote!.Body);
    }

    [Fact]
    public async Task AnimalNoteChanged_SelectedAnimal_NotifiesRereadsAndBroadcasts()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));

        await SelectAnimalAsync(device);
        clients.Invocations.Clear();

        var changes = CaptureNoteChanges(clients);
        StubNotes(deviceStore, "mouse-1", "t2", Note(2, "b2"));
        device.OnAnimalNoteChanged("mouse-1", 2, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Equal(new NoteChangeDto(NoteScope.Behavior, "mouse-1", 2, NoteChangeKind.Created),
            Assert.Single(changes));
        Assert.Equal("t2", device.Animal!.TrainerNotes);
        Assert.Equal("b2", device.Animal.BehaviorNote!.Body);
        clients.Verify(c => c.AnimalChanged(It.IsAny<Animal?>()), Times.Once);
    }

    // AnimalChanged carries the live animal's newest note, so an edit to an older one has nothing to re-publish.
    [Fact]
    public async Task AnimalNoteChanged_OlderNote_NotifiesButDoesNotRebroadcastAnimal()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(9, "newest"));

        await SelectAnimalAsync(device);
        clients.Invocations.Clear();

        var changes = CaptureNoteChanges(clients);
        device.OnAnimalNoteChanged("mouse-1", 3, NoteChangeKind.Edited);
        await DrainAsync(device);

        Assert.Single(changes);
        clients.Verify(c => c.AnimalChanged(It.IsAny<Animal?>()), Times.Never);
    }

    // This is the only path by which a change to a non-selected animal reaches clients at all.
    [Fact]
    public async Task AnimalNoteChanged_OtherAnimal_NotifiesWithoutReadingOrRebroadcasting()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));

        await SelectAnimalAsync(device);
        clients.Invocations.Clear();
        deviceStore.Invocations.Clear();

        var changes = CaptureNoteChanges(clients);
        device.OnAnimalNoteChanged("mouse-2", 7, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Equal(new NoteChangeDto(NoteScope.Behavior, "mouse-2", 7, NoteChangeKind.Created),
            Assert.Single(changes));
        clients.Verify(c => c.AnimalChanged(It.IsAny<Animal?>()), Times.Never);
        deviceStore.Verify(s => s.GetAnimalNotesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnimalNoteChanged_NoAnimalSelected_NotifiesOnly()
    {
        var (device, _, clients, deviceStore, _) = Build();
        var changes = CaptureNoteChanges(clients);

        device.OnAnimalNoteChanged("mouse-1", 4, NoteChangeKind.Deleted);
        await DrainAsync(device);

        Assert.Equal(new NoteChangeDto(NoteScope.Behavior, "mouse-1", 4, NoteChangeKind.Deleted),
            Assert.Single(changes));
        clients.Verify(c => c.AnimalChanged(It.IsAny<Animal?>()), Times.Never);
        deviceStore.Verify(s => s.GetAnimalNotesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Without the forced-same-animal guard in RefreshAnimalNotesAsync's catch, a transient read failure reads as
    // "the note vanished" and a spurious AnimalChanged goes out carrying a null note.
    [Fact]
    public async Task AnimalNoteChanged_RereadFails_NotifiesButKeepsRetainedNote()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));

        await SelectAnimalAsync(device);
        clients.Invocations.Clear();

        var changes = CaptureNoteChanges(clients);
        deviceStore.Setup(s => s.GetAnimalNotesAsync("mouse-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        device.OnAnimalNoteChanged("mouse-1", 2, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Single(changes);
        Assert.Equal("t1", device.Animal!.TrainerNotes);
        Assert.Equal("b1", device.Animal.BehaviorNote!.Body);
        clients.Verify(c => c.AnimalChanged(It.IsAny<Animal?>()), Times.Never);
    }

    // The same failure must not poison the cache: _animalNotesId still matches, so nothing would ever re-read it.
    [Fact]
    public async Task AnimalNoteChanged_RereadFails_CacheStaysUsableAndRecovers()
    {
        var (device, _, _, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));

        await SelectAnimalAsync(device);

        deviceStore.Setup(s => s.GetAnimalNotesAsync("mouse-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        device.OnAnimalNoteChanged("mouse-1", 2, NoteChangeKind.Created);
        await DrainAsync(device);

        // A following count change stamps from the retained cache rather than blanking the animal.
        device.OnApiEvent(Event(ApiEventKind.DayReachCountChanged, new ApiCountChangePayload { Count = 7 }));
        await DrainAsync(device);
        Assert.Equal("b1", device.Animal!.BehaviorNote!.Body);

        // And a following mutation re-reads and recovers.
        StubNotes(deviceStore, "mouse-1", "t2", Note(2, "b2"));
        device.OnAnimalNoteChanged("mouse-1", 2, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Equal("t2", device.Animal!.TrainerNotes);
        Assert.Equal("b2", device.Animal.BehaviorNote!.Body);
    }

    [Fact]
    public async Task AnimalNoteChanged_NotifyThrows_StillRereadsAndBroadcasts()
    {
        var (device, _, clients, deviceStore, _) = Build();
        StubNotes(deviceStore, "mouse-1", "t1", Note(1, "b1"));

        await SelectAnimalAsync(device);
        clients.Invocations.Clear();
        clients.Setup(c => c.NoteChanged(It.IsAny<NoteChangeDto>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        StubNotes(deviceStore, "mouse-1", "t2", Note(2, "b2"));
        device.OnAnimalNoteChanged("mouse-1", 2, NoteChangeKind.Created);
        await DrainAsync(device);

        Assert.Equal("b2", device.Animal!.BehaviorNote!.Body);
        clients.Verify(c => c.AnimalChanged(It.IsAny<Animal?>()), Times.Once);
    }
}
