using System.Net.Http.Json;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

// The task tables over HTTP. Tasks reuse the sync engine wholesale, so what is worth
// pinning down is that they are genuinely separate from the shopping list and that the
// done group resolves independently of content, the way checking off a grocery does.
public class TaskSyncTests : IDisposable
{
    private readonly TestApp _app = new();

    private static TaskEntry NewTask(string title = "call the plumber",
        Priority priority = Priority.Normal, DateOnly? dueOn = null) => new()
    {
        Id = Guid.NewGuid(), TaskListId = TaskList.DefaultId, Title = title,
        Priority = priority, DueOn = dueOn,
    };

    private static async Task<SyncPushResponse> PushAsync(HttpClient http, SyncPushRequest request)
    {
        var response = await http.PostAsJsonAsync("/api/sync", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SyncPushResponse>())!;
    }

    private static Task<SyncPushResponse> PushTasksAsync(HttpClient http, params TaskEntry[] tasks) =>
        PushAsync(http, new SyncPushRequest { Rows = new SyncRows { Tasks = [.. tasks] } });

    private static async Task<SyncPullResponse> PullAsync(HttpClient http, long since = 0) =>
        (await http.GetFromJsonAsync<SyncPullResponse>($"/api/sync?since={since}"))!;

    private static async Task<TaskEntry> ServerTaskAsync(HttpClient http, Guid id) =>
        (await PullAsync(http)).Rows.Tasks.Single(t => t.Id == id);

    [Fact]
    public async Task AFreshInstall_HasOneTaskList_AndNoTasks()
    {
        var sam = (await _app.AddPhoneAsync(TestApp.AdminUsername)).Http;

        var pull = await PullAsync(sam);

        Assert.Equal("Tasks", Assert.Single(pull.Rows.TaskLists).Name);
        Assert.Empty(pull.Rows.Tasks);
    }

    // The reason these are separate tables: neither screen can ever be handed the other's
    // rows, because there is no shared table to forget to filter.
    [Fact]
    public async Task TaskListsAndShoppingListsNeverMix()
    {
        var sam = (await _app.AddPhoneAsync(TestApp.AdminUsername)).Http;

        var pull = await PullAsync(sam);

        Assert.DoesNotContain(pull.Rows.Lists, l => l.Id == TaskList.DefaultId);
        Assert.DoesNotContain(pull.Rows.TaskLists, l => l.Id == ShoppingList.DefaultId);
        Assert.Equal("Groceries", Assert.Single(pull.Rows.Lists).Name);
    }

    [Fact]
    public async Task APushedTask_ComesBackWithWhoAddedIt()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var task = NewTask("book the car in", Priority.High, new DateOnly(2026, 10, 1));

        await PushTasksAsync(sam.Http, task);

        var stored = await ServerTaskAsync(sam.Http, task.Id);
        Assert.Equal("book the car in", stored.Title);
        Assert.Equal(Priority.High, stored.Priority);
        Assert.Equal(new DateOnly(2026, 10, 1), stored.DueOn);
        Assert.Equal(sam.UserId, stored.AddedBy);
        Assert.False(stored.IsDone);
    }

    // The whole reason the done group travels separately: two people, both offline, one
    // reprioritising and one ticking it off, and neither change is lost.
    [Fact]
    public async Task ContentAndDone_AreResolvedIndependently()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var alex = await _app.AddPhoneAsync("alex");
        var task = NewTask("mow the lawn");
        await PushTasksAsync(sam.Http, task);

        var edited = await ServerTaskAsync(sam.Http, task.Id);
        edited.Priority = Priority.High;
        await PushTasksAsync(sam.Http, edited);
        await PushAsync(alex.Http, new SyncPushRequest
        {
            Dones = [new TaskDone(task.Id, true, DateTimeOffset.UtcNow)],
        });

        var stored = await ServerTaskAsync(sam.Http, task.Id);
        Assert.Equal(Priority.High, stored.Priority);
        Assert.True(stored.IsDone);
        Assert.Equal(alex.UserId, stored.DoneBy);
    }

    [Fact]
    public async Task TickingOffIsWhoDidIt_NotWhoeverThePayloadClaims()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var alex = await _app.AddPhoneAsync("alex");
        var task = NewTask();
        await PushTasksAsync(sam.Http, task);

        await PushAsync(alex.Http, new SyncPushRequest
        {
            Dones = [new TaskDone(task.Id, true, DateTimeOffset.UtcNow)],
        });

        Assert.Equal(alex.UserId, (await ServerTaskAsync(sam.Http, task.Id)).DoneBy);
    }

    [Fact]
    public async Task Deletes_AreFinal()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var task = NewTask();
        await PushTasksAsync(sam.Http, task);
        var tombstone = await ServerTaskAsync(sam.Http, task.Id);
        tombstone.IsDeleted = true;
        await PushTasksAsync(sam.Http, tombstone);

        var resurrected = await ServerTaskAsync(sam.Http, task.Id);
        resurrected.IsDeleted = false;
        resurrected.Title = "back from the dead";
        await PushTasksAsync(sam.Http, resurrected);

        var stored = await ServerTaskAsync(sam.Http, task.Id);
        Assert.True(stored.IsDeleted);
        Assert.NotEqual("back from the dead", stored.Title);
    }

    [Theory]
    [InlineData("", "Title is required.")]
    [InlineData("   ", "Title is required.")]
    public async Task ABadTitle_IsRejected(string title, string reason)
    {
        var sam = (await _app.AddPhoneAsync(TestApp.AdminUsername)).Http;
        var task = NewTask(title);

        var response = await PushTasksAsync(sam, task);

        Assert.Equal(reason, Assert.Single(response.Rejections).Reason);
        Assert.Empty((await PullAsync(sam)).Rows.Tasks);
    }

    [Fact]
    public async Task ATaskForAnUnknownList_IsRejected()
    {
        var sam = (await _app.AddPhoneAsync(TestApp.AdminUsername)).Http;
        var task = NewTask();
        task.TaskListId = Guid.NewGuid();

        var response = await PushTasksAsync(sam, task);

        Assert.Equal("Unknown task list.", Assert.Single(response.Rejections).Reason);
    }

    // A shopping list id is not a task list id, however similar the two look.
    [Fact]
    public async Task ATaskCannotBeParkedOnAShoppingList()
    {
        var sam = (await _app.AddPhoneAsync(TestApp.AdminUsername)).Http;
        var task = NewTask();
        task.TaskListId = ShoppingList.DefaultId;

        var response = await PushTasksAsync(sam, task);

        Assert.Equal("Unknown task list.", Assert.Single(response.Rejections).Reason);
    }

    [Fact]
    public async Task ANewTaskList_AndItsTasks_CanArriveTogether()
    {
        var sam = (await _app.AddPhoneAsync(TestApp.AdminUsername)).Http;
        var list = new TaskList { Id = Guid.NewGuid(), Name = "Garden" };
        var task = NewTask("prune the apple tree");
        task.TaskListId = list.Id;

        var response = await PushAsync(sam, new SyncPushRequest
        {
            Rows = new SyncRows { TaskLists = [list], Tasks = [task] },
        });

        Assert.Empty(response.Rejections);
        Assert.Equal(list.Id, (await ServerTaskAsync(sam, task.Id)).TaskListId);
    }

    private static async Task<TaskList> NewListAsync(HttpClient http, string name)
    {
        var list = new TaskList { Id = Guid.NewGuid(), Name = name };
        await PushAsync(http, new SyncPushRequest { Rows = new SyncRows { TaskLists = [list] } });
        return list;
    }

    private static async Task DeleteListOnlyAsync(HttpClient http, TaskList list)
    {
        // Just the list, as a phone would send it if it didn't know any tasks were on it.
        list.IsDeleted = true;
        await PushAsync(http, new SyncPushRequest { Rows = new SyncRows { TaskLists = [list] } });
    }

    [Fact]
    public async Task MovingATask_KeepsEverythingElseAboutIt()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var garden = await NewListAsync(sam.Http, "Garden");
        var task = NewTask("prune the apple tree", Priority.High, new DateOnly(2026, 10, 1));
        await PushTasksAsync(sam.Http, task);
        await PushAsync(sam.Http, new SyncPushRequest { Dones = [new TaskDone(task.Id, true, DateTimeOffset.UtcNow)] });

        var moved = await ServerTaskAsync(sam.Http, task.Id);
        moved.TaskListId = garden.Id;
        var response = await PushTasksAsync(sam.Http, moved);

        Assert.Empty(response.Rejections);
        var stored = await ServerTaskAsync(sam.Http, task.Id);
        Assert.Equal(garden.Id, stored.TaskListId);
        Assert.Equal(Priority.High, stored.Priority);
        Assert.Equal(new DateOnly(2026, 10, 1), stored.DueOn);
        Assert.True(stored.IsDone);
    }

    // The list is deleted first, then a move onto it arrives: refused, and the phone gets
    // back the server's version so the task returns to where it was instead of vanishing.
    [Fact]
    public async Task MovingOntoAJustDeletedList_IsRefused_AndTheTaskStaysPut()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var garden = await NewListAsync(sam.Http, "Garden");
        var task = NewTask();
        await PushTasksAsync(sam.Http, task);
        await DeleteListOnlyAsync(sam.Http, garden);

        var moved = await ServerTaskAsync(sam.Http, task.Id);
        moved.TaskListId = garden.Id;
        var response = await PushTasksAsync(sam.Http, moved);

        Assert.Equal("That task list has been deleted.", Assert.Single(response.Rejections).Reason);
        Assert.Equal(TaskList.DefaultId, Assert.Single(response.Current.Tasks).TaskListId);
        Assert.Equal(TaskList.DefaultId, (await ServerTaskAsync(sam.Http, task.Id)).TaskListId);
    }

    [Fact]
    public async Task AddingToADeletedList_IsRefused()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var garden = await NewListAsync(sam.Http, "Garden");
        await DeleteListOnlyAsync(sam.Http, garden);
        var task = NewTask();
        task.TaskListId = garden.Id;

        var response = await PushTasksAsync(sam.Http, task);

        Assert.Equal("That task list has been deleted.", Assert.Single(response.Rejections).Reason);
    }

    // The other order: the move lands first, then a phone that never saw it deletes the
    // list. That phone's delete only names the tasks it knew about, so the server retires
    // the rest — otherwise the moved task would sit on a deleted list, stored and never shown.
    [Fact]
    public async Task DeletingAList_RetiresATaskAnotherPhoneJustMovedOntoIt()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var alex = await _app.AddPhoneAsync("alex");
        var garden = await NewListAsync(sam.Http, "Garden");
        var task = NewTask("prune the apple tree");
        await PushTasksAsync(alex.Http, task);

        var moved = await ServerTaskAsync(alex.Http, task.Id);
        moved.TaskListId = garden.Id;
        await PushTasksAsync(alex.Http, moved);
        await DeleteListOnlyAsync(sam.Http, garden);

        Assert.True((await ServerTaskAsync(sam.Http, task.Id)).IsDeleted);
    }

    [Fact]
    public async Task DeletingAList_LeavesOtherListsTasksAlone()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var garden = await NewListAsync(sam.Http, "Garden");
        var keep = NewTask("still on Tasks");
        await PushTasksAsync(sam.Http, keep);

        await DeleteListOnlyAsync(sam.Http, garden);

        Assert.False((await ServerTaskAsync(sam.Http, keep.Id)).IsDeleted);
    }

    public void Dispose() => _app.Dispose();
}
