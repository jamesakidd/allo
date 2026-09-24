using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Allo.Tests;

// What an install that is already in use goes through when a release adds tables. Fresh
// databases can't catch these: a new phone pulls from zero and gets everything.
public class UpgradeTests
{
    // The last migration before the tasks feature, i.e. the schema a live install had.
    private const string BeforeTasks = "20260922202322_ItemPendingTags";

    // Stands in for days of grocery syncing: every phone's cursor is well past the seed.
    private const long CursorBeforeUpgrade = 500;

    private static async Task<TestDatabase> UpgradedInstallAsync()
    {
        var database = new TestDatabase(migrateTo: BeforeTasks);
        await using var db = database.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE SyncCounter SET Value = {CursorBeforeUpgrade} WHERE Id = 1");
        await db.Database.MigrateAsync();
        return database;
    }

    // The bug seen on both phones after 0.2.0: the seeded "Tasks" list was stamped at the
    // seed sequence (1), so a phone asking for "everything after 500" never received it.
    // Its tasks arrived — they were stamped when created — but the list itself did not,
    // and the picker showed the list's id and couldn't rename it.
    [Fact]
    public async Task ThePreviouslySeededTaskList_ReachesPhonesThatSyncedBeforeTheUpgrade()
    {
        using var database = await UpgradedInstallAsync();
        await using var db = database.CreateContext();

        var visibleToExistingPhones = await db.TaskLists
            .Where(l => l.Sequence > CursorBeforeUpgrade)
            .Select(l => l.Id)
            .ToListAsync();

        Assert.Contains(TaskList.DefaultId, visibleToExistingPhones);
    }

    // The re-stamped number has to come out of the counter, or the next real change could
    // be handed the same number and a phone would skip one of the two.
    [Fact]
    public async Task TheCounterStaysAheadOfEveryStampedRow()
    {
        using var database = await UpgradedInstallAsync();
        await using var db = database.CreateContext();

        var counter = await db.SyncCounter.Select(c => c.Value).SingleAsync();
        var highest = await db.TaskLists.MaxAsync(l => l.Sequence);

        Assert.True(counter >= highest, $"counter {counter} is behind a row stamped {highest}");
    }

    [Fact]
    public async Task TheListKeepsItsNameThroughTheUpgrade()
    {
        using var database = await UpgradedInstallAsync();
        await using var db = database.CreateContext();

        var list = await db.TaskLists.SingleAsync(l => l.Id == TaskList.DefaultId);

        Assert.Equal("Tasks", list.Name);
        Assert.False(list.IsDeleted);
    }
}
