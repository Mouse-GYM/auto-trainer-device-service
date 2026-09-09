using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Endpoints;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AutoTrainer.Api.Tests.Endpoints;

// The note handlers. The store is mocked -- what it does with a write is DeviceDataStoreNotesTests' job; what
// these cover is the handler's own validation, status codes, and whether the device hook was driven.
public class NotesEndpointTests
{
    private static (AutotrainerDevice device, Mock<IMessageHub> clients, Mock<IDeviceDataStore> deviceStore) Build()
    {
        var clients = new Mock<IMessageHub>();
        var hub = new Mock<IHubContext<MessageHub, IMessageHub>>();
        var hubClients = new Mock<IHubClients<IMessageHub>>();
        hubClients.Setup(c => c.All).Returns(clients.Object);
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        var deviceStore = new Mock<IDeviceDataStore>();

        // Both note hooks re-read the store; an unstubbed Task<T> completes with null.
        deviceStore.Setup(s => s.GetAnimalNotesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnimalNotes.Empty);
        deviceStore.Setup(s => s.GetLatestSystemNoteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteDto?)null);

        var device = new AutotrainerDevice(
            new Mock<ICommandTaskQueue>().Object, hub.Object, NullLogger<AutotrainerDevice>.Instance,
            deviceStore.Object, new Mock<IAnimalDataStore>().Object);

        return (device, clients, deviceStore);
    }

    // DeviceUpdateWorker isn't running in tests; execute the queued action(s) ourselves.
    private static async Task DrainAsync(AutotrainerDevice device)
    {
        while (device.UpdateReader.TryRead(out var action))
            await action();
    }

    private static int QueuedCount(AutotrainerDevice device)
    {
        var count = 0;
        while (device.UpdateReader.TryRead(out _))
            count++;

        return count;
    }

    private static NoteDto Note(int id, string body) =>
        new(id, body, null, DateTime.UtcNow, DateTime.UtcNow);

    private static NoteWriteResult Ok(NoteDto? note) => new(NoteWriteOutcome.Ok, note);

    private static NoteWriteResult Miss(NoteWriteOutcome outcome) => new(outcome, null);

    private static string TooLong => new('x', NotesText.MaxLength + 1);

    // ---- Device: GET ----

    [Fact]
    public async Task GetSystemNotes_UnsupportedSort_Returns400_WithoutReading()
    {
        var (_, _, deviceStore) = Build();

        var result = await DeviceEndpoints.GetSystemNotes("body", null, null, deviceStore.Object, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.GetSystemNotesAsync(
            It.IsAny<SortRequest>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSystemNotes_Succeeds_Returns200()
    {
        var (_, _, deviceStore) = Build();
        var page = new PagedResult<NoteDto>([Note(1, "a")], 1, 50, 1);
        deviceStore.Setup(s => s.GetSystemNotesAsync(
                It.IsAny<SortRequest>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var result = await DeviceEndpoints.GetSystemNotes(null, null, null, deviceStore.Object, default);

        Assert.Same(page, Assert.IsType<Ok<PagedResult<NoteDto>>>(result).Value);
    }

    // ---- Device: POST ----

    [Fact]
    public async Task CreateSystemNote_NullBody_Returns400_WithoutWriting()
    {
        var (device, _, deviceStore) = Build();

        var result = await DeviceEndpoints.CreateSystemNote(null, deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.AddSystemNoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, QueuedCount(device));
    }

    [Fact]
    public async Task CreateSystemNote_BlankBody_Returns400_WithoutWriting()
    {
        var (device, _, deviceStore) = Build();

        var result = await DeviceEndpoints.CreateSystemNote(new NoteWrite("   "), deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.AddSystemNoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateSystemNote_OverLengthCap_Returns400_WithoutWriting()
    {
        var (device, _, deviceStore) = Build();

        var result = await DeviceEndpoints.CreateSystemNote(new NoteWrite(TooLong), deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.AddSystemNoteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateSystemNote_Succeeds_Returns201_AndQueuesOneUpdate()
    {
        var (device, _, deviceStore) = Build();
        var note = Note(7, "bench 3");
        deviceStore.Setup(s => s.AddSystemNoteAsync("bench 3", It.IsAny<CancellationToken>())).ReturnsAsync(note);

        var result = await DeviceEndpoints.CreateSystemNote(new NoteWrite("bench 3"), deviceStore.Object, device, default);

        var created = Assert.IsType<Created<NoteDto>>(result);
        Assert.Same(note, created.Value);
        Assert.Equal("/device/systemnotes/7", created.Location);
        Assert.Equal(1, QueuedCount(device));
    }

    // ---- Device: PATCH ----

    [Fact]
    public async Task EditSystemNote_BlankBody_Returns400_WithoutWriting()
    {
        var (device, _, deviceStore) = Build();

        var result = await DeviceEndpoints.EditSystemNote(7, new NoteWrite(""), deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.EditSystemNoteAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EditSystemNote_UnknownNote_Returns404_AndFiresNoHook()
    {
        var (device, _, deviceStore) = Build();
        deviceStore.Setup(s => s.EditSystemNoteAsync(7, "x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Miss(NoteWriteOutcome.NoSuchNote));

        var result = await DeviceEndpoints.EditSystemNote(7, new NoteWrite("x"), deviceStore.Object, device, default);

        Assert.IsType<NotFound<string>>(result);
        Assert.Equal(0, QueuedCount(device));
    }

    [Fact]
    public async Task EditSystemNote_Succeeds_Returns200_AndQueuesOneUpdate()
    {
        var (device, _, deviceStore) = Build();
        var note = Note(7, "edited");
        deviceStore.Setup(s => s.EditSystemNoteAsync(7, "edited", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(note));

        var result = await DeviceEndpoints.EditSystemNote(7, new NoteWrite("edited"), deviceStore.Object, device, default);

        Assert.Same(note, Assert.IsType<Ok<NoteDto>>(result).Value);
        Assert.Equal(1, QueuedCount(device));
    }

    // ---- Device: DELETE ----

    [Fact]
    public async Task DeleteSystemNote_UnknownNote_Returns404_AndFiresNoHook()
    {
        var (device, _, deviceStore) = Build();
        deviceStore.Setup(s => s.DeleteSystemNoteAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Miss(NoteWriteOutcome.NoSuchNote));

        var result = await DeviceEndpoints.DeleteSystemNote(7, deviceStore.Object, device, default);

        Assert.IsType<NotFound<string>>(result);
        Assert.Equal(0, QueuedCount(device));
    }

    // QueuedCount alone would stay green against a handler that dereferenced the null result note -- that throws
    // before the enqueue -- so this asserts on the delivered NoteChangeDto instead.
    [Fact]
    public async Task DeleteSystemNote_Succeeds_Returns204_AndNotifiesWithRouteNoteId()
    {
        var (device, clients, deviceStore) = Build();
        deviceStore.Setup(s => s.DeleteSystemNoteAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(null));

        var changes = new List<NoteChangeDto>();
        clients.Setup(c => c.NoteChanged(It.IsAny<NoteChangeDto>()))
            .Callback<NoteChangeDto>(changes.Add).Returns(Task.CompletedTask);

        var result = await DeviceEndpoints.DeleteSystemNote(7, deviceStore.Object, device, default);
        await DrainAsync(device);

        Assert.IsType<NoContent>(result);
        Assert.Equal(new NoteChangeDto(NoteScope.System, null, 7, NoteChangeKind.Deleted), Assert.Single(changes));
    }

    // ---- Animal: GET ----

    [Fact]
    public async Task GetBehaviorNotes_InvalidIdentifier_Returns400_WithoutReading()
    {
        var (_, _, deviceStore) = Build();

        var result = await AnimalEndpoints.GetBehaviorNotes("../escape", null, null, null, deviceStore.Object, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.GetBehaviorNotesAsync(It.IsAny<string>(), It.IsAny<SortRequest>(),
            It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBehaviorNotes_UnsupportedSort_Returns400()
    {
        var (_, _, deviceStore) = Build();

        var result = await AnimalEndpoints.GetBehaviorNotes("mouse-1", "body", null, null, deviceStore.Object, default);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task GetBehaviorNotes_UnknownAnimal_Returns404()
    {
        var (_, _, deviceStore) = Build();
        deviceStore.Setup(s => s.GetBehaviorNotesAsync("mouse-1", It.IsAny<SortRequest>(),
                It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<NoteDto>?)null);

        var result = await AnimalEndpoints.GetBehaviorNotes("mouse-1", null, null, null, deviceStore.Object, default);

        Assert.IsType<NotFound<string>>(result);
    }

    // A registered animal with no notes is an empty page, not a 404 -- the distinction the store draws.
    [Fact]
    public async Task GetBehaviorNotes_EmptyLog_Returns200()
    {
        var (_, _, deviceStore) = Build();
        deviceStore.Setup(s => s.GetBehaviorNotesAsync("mouse-1", It.IsAny<SortRequest>(),
                It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<NoteDto>.Empty(1, 50));

        var result = await AnimalEndpoints.GetBehaviorNotes("mouse-1", null, null, null, deviceStore.Object, default);

        Assert.Empty(Assert.IsType<Ok<PagedResult<NoteDto>>>(result).Value!.Items);
    }

    // ---- Animal: POST ----

    [Fact]
    public async Task CreateBehaviorNote_InvalidIdentifier_Returns400_WithoutWriting()
    {
        var (device, _, deviceStore) = Build();

        var result = await AnimalEndpoints.CreateBehaviorNote("../escape", new NoteWrite("skittish"),
            deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.AddBehaviorNoteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBehaviorNote_NullBody_Returns400()
    {
        var (device, _, deviceStore) = Build();

        var result = await AnimalEndpoints.CreateBehaviorNote("mouse-1", null, deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task CreateBehaviorNote_BlankBody_Returns400_WithoutWriting()
    {
        var (device, _, deviceStore) = Build();

        var result = await AnimalEndpoints.CreateBehaviorNote("mouse-1", new NoteWrite(" "),
            deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.AddBehaviorNoteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBehaviorNote_OverLengthCap_Returns400_WithoutWriting()
    {
        var (device, _, deviceStore) = Build();

        var result = await AnimalEndpoints.CreateBehaviorNote("mouse-1", new NoteWrite(TooLong),
            deviceStore.Object, device, default);

        Assert.IsType<BadRequest<string>>(result);
        deviceStore.Verify(s => s.AddBehaviorNoteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBehaviorNote_UnknownAnimal_Returns404_AndFiresNoHook()
    {
        var (device, _, deviceStore) = Build();
        deviceStore.Setup(s => s.AddBehaviorNoteAsync("mouse-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Miss(NoteWriteOutcome.NoSuchAnimal));

        var result = await AnimalEndpoints.CreateBehaviorNote("mouse-1", new NoteWrite("skittish"),
            deviceStore.Object, device, default);

        Assert.IsType<NotFound<string>>(result);
        Assert.Equal(0, QueuedCount(device));
    }

    [Fact]
    public async Task CreateBehaviorNote_Succeeds_Returns201_AndQueuesOneUpdate()
    {
        var (device, _, deviceStore) = Build();
        var note = Note(3, "skittish after 16:00");
        deviceStore.Setup(s => s.AddBehaviorNoteAsync("mouse-1", "skittish after 16:00", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(note));

        var result = await AnimalEndpoints.CreateBehaviorNote("mouse-1", new NoteWrite("skittish after 16:00"),
            deviceStore.Object, device, default);

        var created = Assert.IsType<Created<NoteDto>>(result);
        Assert.Same(note, created.Value);
        Assert.Equal("/animals/mouse-1/behaviornotes/3", created.Location);
        Assert.Equal(1, QueuedCount(device));
    }

    // ---- Animal: PATCH ----

    [Fact]
    public async Task EditBehaviorNote_UnknownAnimal_Returns404_AndFiresNoHook()
    {
        var (device, _, deviceStore) = Build();
        deviceStore.Setup(s => s.EditBehaviorNoteAsync("mouse-1", 3, "x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Miss(NoteWriteOutcome.NoSuchAnimal));

        var result = await AnimalEndpoints.EditBehaviorNote("mouse-1", 3, new NoteWrite("x"),
            deviceStore.Object, device, default);

        Assert.IsType<NotFound<string>>(result);
        Assert.Equal(0, QueuedCount(device));
    }

    // The two 404s must read differently, which is why the store distinguishes them.
    [Fact]
    public async Task EditBehaviorNote_UnknownNote_Returns404_NamingTheNote()
    {
        var (device, _, deviceStore) = Build();
        deviceStore.Setup(s => s.EditBehaviorNoteAsync("mouse-1", 3, "x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Miss(NoteWriteOutcome.NoSuchNote));

        var result = await AnimalEndpoints.EditBehaviorNote("mouse-1", 3, new NoteWrite("x"),
            deviceStore.Object, device, default);

        Assert.Contains("note 3", Assert.IsType<NotFound<string>>(result).Value);
        Assert.Equal(0, QueuedCount(device));
    }

    [Fact]
    public async Task EditBehaviorNote_Succeeds_Returns200_AndQueuesOneUpdate()
    {
        var (device, _, deviceStore) = Build();
        var note = Note(3, "edited");
        deviceStore.Setup(s => s.EditBehaviorNoteAsync("mouse-1", 3, "edited", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(note));

        var result = await AnimalEndpoints.EditBehaviorNote("mouse-1", 3, new NoteWrite("edited"),
            deviceStore.Object, device, default);

        Assert.Same(note, Assert.IsType<Ok<NoteDto>>(result).Value);
        Assert.Equal(1, QueuedCount(device));
    }

    // ---- Animal: DELETE ----

    [Fact]
    public async Task DeleteBehaviorNote_UnknownNote_Returns404_AndFiresNoHook()
    {
        var (device, _, deviceStore) = Build();
        deviceStore.Setup(s => s.DeleteBehaviorNoteAsync("mouse-1", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Miss(NoteWriteOutcome.NoSuchNote));

        var result = await AnimalEndpoints.DeleteBehaviorNote("mouse-1", 3, deviceStore.Object, device, default);

        Assert.IsType<NotFound<string>>(result);
        Assert.Equal(0, QueuedCount(device));
    }

    [Fact]
    public async Task DeleteBehaviorNote_Succeeds_Returns204_AndNotifiesWithRouteNoteId()
    {
        var (device, clients, deviceStore) = Build();
        deviceStore.Setup(s => s.DeleteBehaviorNoteAsync("mouse-1", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(null));

        var changes = new List<NoteChangeDto>();
        clients.Setup(c => c.NoteChanged(It.IsAny<NoteChangeDto>()))
            .Callback<NoteChangeDto>(changes.Add).Returns(Task.CompletedTask);

        var result = await AnimalEndpoints.DeleteBehaviorNote("mouse-1", 3, deviceStore.Object, device, default);
        await DrainAsync(device);

        Assert.IsType<NoContent>(result);
        Assert.Equal(new NoteChangeDto(NoteScope.Behavior, "mouse-1", 3, NoteChangeKind.Deleted),
            Assert.Single(changes));
    }
}
