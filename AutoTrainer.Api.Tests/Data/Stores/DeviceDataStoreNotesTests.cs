using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoTrainer.Api.Tests.Data.Stores;

// The two note logs against a real migrated device database. Endpoint-level concerns (status codes, hook
// firing) are NotesEndpointTests' job.
public class DeviceDataStoreNotesTests
{
    private static readonly SortRequest NoSort = SortRequest.From(null);
    private static readonly PageRequest FirstPage = PageRequest.From(null, null);

    private static (TestDeviceDbContextFactory factory, DeviceDataStore store) NewStore() =>
        TestDeviceStore.CreateMigrated();

    // Backdates a note's CreatedAt so ordering assertions do not depend on how fast the inserts run. The
    // Modified audit only bumps UpdatedAt, so the backdated CreatedAt survives the save.
    private static async Task BackdateAsync(TestDeviceDbContextFactory factory, int noteId, DateTime createdAt,
        bool behavior = false)
    {
        using var db = factory.CreateDbContext();

        if (behavior)
        {
            var row = db.BehaviorNotes.Single(n => n.Id == noteId);
            row.CreatedAt = createdAt;
        }
        else
        {
            var row = db.SystemNotes.Single(n => n.Id == noteId);
            row.CreatedAt = createdAt;
        }

        await db.SaveChangesAsync();
    }

    // ---- System notes ----

    [Fact]
    public async Task AddSystemNote_ReturnsTheNote_Unedited()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var note = await store.AddSystemNoteAsync("bench 3, left rack");

            Assert.NotEqual(0, note.Id);
            Assert.Equal("bench 3, left rack", note.Body);
            Assert.Null(note.AuthorId);
            Assert.Equal(note.WrittenAt, note.EditedAt);
            Assert.NotEqual(default, note.WrittenAt);
        }
    }

    [Fact]
    public async Task GetSystemNotes_NewestFirstByDefault_AndSortReverses()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            var first = await store.AddSystemNoteAsync("first");
            var second = await store.AddSystemNoteAsync("second");
            await BackdateAsync(factory, first.Id, now.AddMinutes(-2));
            await BackdateAsync(factory, second.Id, now.AddMinutes(-1));

            var byDefault = await store.GetSystemNotesAsync(NoSort, FirstPage);
            Assert.Equal(["second", "first"], byDefault.Items.Select(n => n.Body));

            var ascending = await store.GetSystemNotesAsync(SortRequest.From("createdAt"), FirstPage);
            Assert.Equal(["first", "second"], ascending.Items.Select(n => n.Body));

            // An explicit descending request matches the default.
            var descending = await store.GetSystemNotesAsync(SortRequest.From("-createdAt"), FirstPage);
            Assert.Equal(["second", "first"], descending.Items.Select(n => n.Body));
        }
    }

    [Fact]
    public void NoteSort_RejectsUnsupportedField_AndAllowsNoSort()
    {
        Assert.True(NoteSort.IsSupported(SortRequest.From(null)));
        Assert.True(NoteSort.IsSupported(SortRequest.From("createdAt")));
        Assert.True(NoteSort.IsSupported(SortRequest.From("-CREATEDAT")));   // matched case-insensitively
        Assert.False(NoteSort.IsSupported(SortRequest.From("body")));
    }

    [Fact]
    public async Task GetSystemNotes_TotalCountsTheWholeLog_NotThePage()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                var note = await store.AddSystemNoteAsync($"note {i}");
                await BackdateAsync(factory, note.Id, now.AddMinutes(-3 + i));
            }

            var page1 = await store.GetSystemNotesAsync(NoSort, PageRequest.From(1, 2));
            Assert.Equal(3, page1.TotalCount);
            Assert.Equal(2, page1.Items.Count);

            var page2 = await store.GetSystemNotesAsync(NoSort, PageRequest.From(2, 2));
            Assert.Equal(3, page2.TotalCount);
            Assert.Equal("note 0", Assert.Single(page2.Items).Body);
        }
    }

    [Fact]
    public async Task EditSystemNote_ChangesBody_AndBumpsEditedAt()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var note = await store.AddSystemNoteAsync("before");

            var result = await store.EditSystemNoteAsync(note.Id, "after");

            Assert.Equal(NoteWriteOutcome.Ok, result.Outcome);
            Assert.Equal("after", result.Note!.Body);
            Assert.Equal(note.WrittenAt, result.Note.WrittenAt);
            Assert.True(result.Note.EditedAt >= note.EditedAt);
        }
    }

    [Fact]
    public async Task EditSystemNote_SameBody_DoesNotBumpEditedAt()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var note = await store.AddSystemNoteAsync("same");

            var result = await store.EditSystemNoteAsync(note.Id, "same");

            Assert.Equal(NoteWriteOutcome.Ok, result.Outcome);
            Assert.Equal(note.EditedAt, result.Note!.EditedAt);
        }
    }

    [Fact]
    public async Task EditSystemNote_UnknownId_IsNoSuchNote()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var result = await store.EditSystemNoteAsync(404, "x");

            Assert.Equal(NoteWriteOutcome.NoSuchNote, result.Outcome);
            Assert.Null(result.Note);
        }
    }

    [Fact]
    public async Task DeleteSystemNote_HidesTheNote_ButKeepsTheRow()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var note = await store.AddSystemNoteAsync("gone");

            var result = await store.DeleteSystemNoteAsync(note.Id);

            Assert.Equal(NoteWriteOutcome.Ok, result.Outcome);
            Assert.Null(result.Note);   // nothing to return: the row is filtered out from here on
            Assert.Empty((await store.GetSystemNotesAsync(NoSort, FirstPage)).Items);
            Assert.Null(await store.GetLatestSystemNoteAsync());

            using var db = factory.CreateDbContext();
            var row = Assert.Single(db.SystemNotes.IgnoreQueryFilters().ToList());
            Assert.NotNull(row.DeletedAt);
        }
    }

    // The row is already filtered out, so a second delete is a 404 -- deliberately not idempotent.
    [Fact]
    public async Task DeleteSystemNote_Twice_IsNoSuchNote()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var note = await store.AddSystemNoteAsync("gone");

            await store.DeleteSystemNoteAsync(note.Id);

            Assert.Equal(NoteWriteOutcome.NoSuchNote, (await store.DeleteSystemNoteAsync(note.Id)).Outcome);
        }
    }

    [Fact]
    public async Task GetLatestSystemNote_IsTheNewest_AndFallsBackWhenItIsDeleted()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            Assert.Null(await store.GetLatestSystemNoteAsync());

            var now = DateTime.UtcNow;
            var older = await store.AddSystemNoteAsync("older");
            var newer = await store.AddSystemNoteAsync("newer");
            await BackdateAsync(factory, older.Id, now.AddMinutes(-2));
            await BackdateAsync(factory, newer.Id, now.AddMinutes(-1));

            Assert.Equal("newer", (await store.GetLatestSystemNoteAsync())!.Body);

            await store.DeleteSystemNoteAsync(newer.Id);

            Assert.Equal("older", (await store.GetLatestSystemNoteAsync())!.Body);
        }
    }

    // ---- Behavior notes ----

    [Fact]
    public async Task BehaviorNotes_UnregisteredAnimal_IsNullOrNoSuchAnimal()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            Assert.Null(await store.GetBehaviorNotesAsync("nobody", NoSort, FirstPage));
            Assert.Equal(NoteWriteOutcome.NoSuchAnimal, (await store.AddBehaviorNoteAsync("nobody", "x")).Outcome);
            Assert.Equal(NoteWriteOutcome.NoSuchAnimal, (await store.EditBehaviorNoteAsync("nobody", 1, "x")).Outcome);
            Assert.Equal(NoteWriteOutcome.NoSuchAnimal, (await store.DeleteBehaviorNoteAsync("nobody", 1)).Outcome);

            // Nothing was minted by any of those calls.
            Assert.Empty(await store.GetAnimalsAsync());
        }
    }

    // The distinction the endpoint depends on: a registered animal with no notes is an empty page, not a 404.
    [Fact]
    public async Task GetBehaviorNotes_RegisteredAnimalWithNoNotes_IsAnEmptyPage()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");

            var page = await store.GetBehaviorNotesAsync("id-a", NoSort, FirstPage);

            Assert.NotNull(page);
            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalCount);
        }
    }

    [Fact]
    public async Task GetBehaviorNotes_NewestFirstByDefault_AndSortReverses()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");

            var now = DateTime.UtcNow;
            var first = await store.AddBehaviorNoteAsync("id-a", "first");
            var second = await store.AddBehaviorNoteAsync("id-a", "second");
            await BackdateAsync(factory, first.Note!.Id, now.AddMinutes(-2), behavior: true);
            await BackdateAsync(factory, second.Note!.Id, now.AddMinutes(-1), behavior: true);

            var byDefault = await store.GetBehaviorNotesAsync("id-a", NoSort, FirstPage);
            Assert.Equal(["second", "first"], byDefault!.Items.Select(n => n.Body));

            var ascending = await store.GetBehaviorNotesAsync("id-a", SortRequest.From("createdAt"), FirstPage);
            Assert.Equal(["first", "second"], ascending!.Items.Select(n => n.Body));
        }
    }

    // A note id belonging to animal A must not be reachable through animal B on either write.
    [Fact]
    public async Task BehaviorNotes_AreScopedToTheirAnimal()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");
            await store.SetAnimalNameAsync("id-b", "Beta");

            var note = (await store.AddBehaviorNoteAsync("id-a", "skittish after 16:00")).Note!;

            Assert.Equal(NoteWriteOutcome.NoSuchNote, (await store.EditBehaviorNoteAsync("id-b", note.Id, "x")).Outcome);
            Assert.Equal(NoteWriteOutcome.NoSuchNote, (await store.DeleteBehaviorNoteAsync("id-b", note.Id)).Outcome);

            // Untouched by either attempt.
            var page = await store.GetBehaviorNotesAsync("id-a", NoSort, FirstPage);
            Assert.Equal("skittish after 16:00", Assert.Single(page!.Items).Body);
        }
    }

    [Fact]
    public async Task EditBehaviorNote_ChangesBody_AndSameBodyDoesNotBumpEditedAt()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");
            var note = (await store.AddBehaviorNoteAsync("id-a", "before")).Note!;

            var edited = await store.EditBehaviorNoteAsync("id-a", note.Id, "after");
            Assert.Equal("after", edited.Note!.Body);
            Assert.True(edited.Note.EditedAt >= note.EditedAt);

            var again = await store.EditBehaviorNoteAsync("id-a", note.Id, "after");
            Assert.Equal(edited.Note.EditedAt, again.Note!.EditedAt);
        }
    }

    [Fact]
    public async Task DeleteBehaviorNote_HidesTheNote_ButKeepsTheRow()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");
            var note = (await store.AddBehaviorNoteAsync("id-a", "gone")).Note!;

            Assert.Equal(NoteWriteOutcome.Ok, (await store.DeleteBehaviorNoteAsync("id-a", note.Id)).Outcome);

            Assert.Empty((await store.GetBehaviorNotesAsync("id-a", NoSort, FirstPage))!.Items);
            Assert.Null((await store.GetAnimalNotesAsync("id-a")).BehaviorNote);
            Assert.Equal(NoteWriteOutcome.NoSuchNote, (await store.DeleteBehaviorNoteAsync("id-a", note.Id)).Outcome);

            using var db = factory.CreateDbContext();
            var row = Assert.Single(db.BehaviorNotes.IgnoreQueryFilters().ToList());
            Assert.NotNull(row.DeletedAt);
        }
    }

    [Fact]
    public async Task DeletingTheNewestNote_MakesTheNextNewestLatest()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");

            var now = DateTime.UtcNow;
            var older = (await store.AddBehaviorNoteAsync("id-a", "older")).Note!;
            var newer = (await store.AddBehaviorNoteAsync("id-a", "newer")).Note!;
            await BackdateAsync(factory, older.Id, now.AddMinutes(-2), behavior: true);
            await BackdateAsync(factory, newer.Id, now.AddMinutes(-1), behavior: true);

            Assert.Equal("newer", (await store.GetAnimalNotesAsync("id-a")).BehaviorNote!.Body);

            await store.DeleteBehaviorNoteAsync("id-a", newer.Id);

            Assert.Equal("older", (await store.GetAnimalNotesAsync("id-a")).BehaviorNote!.Body);
        }
    }

    // The query filter hides the animal, so the writes report NoSuchAnimal -- while its note rows survive.
    [Fact]
    public async Task SoftDeletedAnimal_ReadsAsNoSuchAnimal_AndItsNotesSurvive()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");
            await store.AddBehaviorNoteAsync("id-a", "skittish after 16:00");

            using (var db = factory.CreateDbContext())
            {
                db.Animals.Remove(db.Animals.Single(a => a.Identifier == "id-a"));
                await db.SaveChangesAsync();
            }

            Assert.Null(await store.GetBehaviorNotesAsync("id-a", NoSort, FirstPage));
            Assert.Equal(NoteWriteOutcome.NoSuchAnimal, (await store.AddBehaviorNoteAsync("id-a", "x")).Outcome);

            using var check = factory.CreateDbContext();
            Assert.Single(check.BehaviorNotes.ToList());
        }
    }

    [Fact]
    public async Task GetAnimalNotes_CarriesTrainerNotesAndTheNewestNote()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");

            using (var db = factory.CreateDbContext())
            {
                db.Animals.Single(a => a.Identifier == "id-a").TrainerNotes = "prefers left paw";
                await db.SaveChangesAsync();
            }

            await store.AddBehaviorNoteAsync("id-a", "skittish after 16:00");

            var notes = await store.GetAnimalNotesAsync("id-a");

            Assert.Equal("prefers left paw", notes.TrainerNotes);
            Assert.Equal("skittish after 16:00", notes.BehaviorNote!.Body);
        }
    }

    // An animal can be selected before anything registers it, so no row is not an error.
    [Fact]
    public async Task GetAnimalNotes_UnknownAnimal_IsEmpty()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            var notes = await store.GetAnimalNotesAsync("nobody");

            Assert.Equal("", notes.TrainerNotes);
            Assert.Null(notes.BehaviorNote);
        }
    }

    [Fact]
    public async Task GetAnimals_CarriesTheNewestNote_AndTheRegistryId()
    {
        var (factory, store) = NewStore();
        using (factory)
        {
            await store.SetAnimalNameAsync("id-a", "Alpha");
            await store.SetAnimalNameAsync("id-b", "Beta");

            var now = DateTime.UtcNow;
            var older = (await store.AddBehaviorNoteAsync("id-a", "older")).Note!;
            var newer = (await store.AddBehaviorNoteAsync("id-a", "newer")).Note!;
            await BackdateAsync(factory, older.Id, now.AddMinutes(-2), behavior: true);
            await BackdateAsync(factory, newer.Id, now.AddMinutes(-1), behavior: true);

            var animals = await store.GetAnimalsAsync();

            Assert.Equal("newer", animals[0].BehaviorNote!.Body);
            Assert.NotEqual(0, animals[0].Id);

            // An animal with no notes carries a null note, not an empty one.
            Assert.Null(animals[1].BehaviorNote);
        }
    }
}
