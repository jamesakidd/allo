using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

// Two phones and a task list, going in and out of airplane mode. Tasks reuse the sync
// engine, so what these prove is that the wiring is right — that the done group really
// does travel separately, and that a task edited with no signal is not lost.
public class TaskOfflineTests : IDisposable
{
    private readonly TestApp _app = new();

    private async Task<(Phone Sam, Phone Alex)> TwoSyncedPhonesAsync()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var alex = await _app.AddPhoneAsync("alex");
        await sam.SyncAsync();
        await alex.SyncAsync();
        return (sam, alex);
    }

    private static async Task<TaskEntry> AddTaskAsync(Phone phone, string title,
        Priority priority = Priority.Normal)
    {
        var task = new TaskEntry
        {
            Id = Guid.NewGuid(), TaskListId = TaskList.DefaultId, Title = title,
            Priority = priority, AddedBy = phone.UserId,
        };
        await phone.Store.SaveAsync(task);
        return task;
    }

    private static TaskEntry Task(Phone phone, Guid id) => phone.Store.Tasks.Single(t => t.Id == id);

    [Fact]
    public async Task FirstSync_BringsTheSeededTaskList()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);

        await sam.SyncAsync();

        Assert.Equal("Tasks", Assert.Single(sam.Store.TaskLists).Name);
        Assert.Empty(sam.Store.Tasks);
    }

    [Fact]
    public async Task ATaskAddedOnOnePhone_ReachesTheOther()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();

        var task = await AddTaskAsync(sam, "book the car in", Priority.High);
        await sam.SyncAsync();
        await alex.SyncAsync();

        var theirs = Task(alex, task.Id);
        Assert.Equal("book the car in", theirs.Title);
        Assert.Equal(Priority.High, theirs.Priority);
        Assert.Equal(0, sam.Store.PendingCount);
    }

    // The task version of the case the two field groups exist for: one person reprioritising
    // with no signal, another ticking it off, and neither change lost.
    [Fact]
    public async Task AirplaneMode_ReprioritiseAndComplete_BothSurvive()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();
        var task = await AddTaskAsync(sam, "mow the lawn");
        await sam.SyncAsync();
        await alex.SyncAsync();

        sam.Network.Offline = true;
        var samTask = Task(sam, task.Id);
        samTask.Priority = Priority.High;
        await sam.Store.SaveAsync(samTask);
        await sam.SyncAsync();
        Assert.Equal(SyncState.Offline, sam.Engine.State);
        Assert.Equal(1, sam.Store.PendingCount);

        await alex.Store.SetDoneAsync(task.Id, true, alex.UserId);
        await alex.SyncAsync();

        sam.Network.Offline = false;
        await sam.SyncAsync();
        await alex.SyncAsync();

        foreach (var phone in new[] { sam, alex })
        {
            Assert.Equal(Priority.High, Task(phone, task.Id).Priority);
            Assert.True(Task(phone, task.Id).IsDone);
            Assert.Equal(0, phone.Store.PendingCount);
        }
    }

    [Fact]
    public async Task ATaskAddedOffline_GoesUpOnTheNextSync()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();

        sam.Network.Offline = true;
        var task = await AddTaskAsync(sam, "fix the gate");
        await sam.SyncAsync();
        Assert.Equal(1, sam.Store.PendingCount);
        // Visible on the device immediately, signal or not.
        Assert.Equal("fix the gate", Task(sam, task.Id).Title);

        sam.Network.Offline = false;
        await sam.SyncAsync();
        await alex.SyncAsync();

        Assert.Equal("fix the gate", Task(alex, task.Id).Title);
        Assert.Equal(0, sam.Store.PendingCount);
    }

    [Fact]
    public async Task ATaskQueueSurvivesClosingTheApp()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();
        sam.Network.Offline = true;
        var task = await AddTaskAsync(sam, "renew the insurance");

        await sam.ReloadAsync();

        Assert.Equal(1, sam.Store.PendingCount);
        sam.Network.Offline = false;
        await sam.SyncAsync();
        await alex.SyncAsync();
        Assert.Equal("renew the insurance", Task(alex, task.Id).Title);
    }

    // Moving is a content change and ticking off is the done group, so they never collide.
    [Fact]
    public async Task AirplaneMode_MoveAndComplete_BothSurvive()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();
        var garden = new TaskList { Id = Guid.NewGuid(), Name = "Garden" };
        await sam.Store.SaveAsync(garden);
        var task = await AddTaskAsync(sam, "prune the apple tree");
        await sam.SyncAsync();
        await alex.SyncAsync();

        sam.Network.Offline = true;
        var samTask = Task(sam, task.Id);
        samTask.TaskListId = garden.Id;
        await sam.Store.SaveAsync(samTask);
        await sam.SyncAsync();

        await alex.Store.SetDoneAsync(task.Id, true, alex.UserId);
        await alex.SyncAsync();

        sam.Network.Offline = false;
        await sam.SyncAsync();
        await alex.SyncAsync();

        foreach (var phone in new[] { sam, alex })
        {
            Assert.Equal(garden.Id, Task(phone, task.Id).TaskListId);
            Assert.True(Task(phone, task.Id).IsDone);
            Assert.Equal(0, phone.Store.PendingCount);
        }
    }

    public void Dispose() => _app.Dispose();
}
