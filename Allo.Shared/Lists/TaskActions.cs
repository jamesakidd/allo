using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Shared.Lists;

// One task moved by a batch move: enough to put it back where it was.
public sealed record TaskMove(Guid TaskId, Guid FromListId);

// Every task action. Like ListActions, they write to the device and return at once;
// syncing happens after, so nothing here waits on a network.
public sealed class TaskActions(LocalStore store, TimeProvider time)
{
    public async Task<TaskEntry> AddAsync(string title, Guid taskListId, Guid userId,
        Priority priority = Priority.Normal, DateOnly? dueOn = null)
    {
        var task = new TaskEntry
        {
            Id = Guid.NewGuid(),
            TaskListId = taskListId,
            Title = title.Trim(),
            Priority = priority,
            DueOn = dueOn,
            AddedBy = userId,
            UpdatedAt = time.GetUtcNow(),
        };
        await store.SaveAsync(task);
        return task;
    }

    public Task SaveAsync(TaskEntry task) => store.SaveAsync(task);

    // Moves a task to another of this device's lists, keeping everything else about it —
    // priority, due date, done or not. False when the target isn't a live list here, which
    // is what Undo hits if the original list was deleted in the meantime.
    public async Task<bool> MoveAsync(TaskEntry task, Guid taskListId)
    {
        if (task.TaskListId == taskListId || !TaskView.Lists(store).Any(l => l.Id == taskListId))
        {
            return false;
        }
        task.TaskListId = taskListId;
        await store.SaveAsync(task);
        return true;
    }

    // Moves several tasks at once. Returns where each moved task came from, so Undo can send
    // every one back to its own list; tasks already on the target, or a target that isn't a
    // live list here, are left alone and not reported.
    public async Task<IReadOnlyList<TaskMove>> MoveManyAsync(IEnumerable<TaskEntry> tasks, Guid taskListId)
    {
        var moves = new List<TaskMove>();
        foreach (var task in tasks.ToList())
        {
            var from = task.TaskListId;
            if (await MoveAsync(task, taskListId))
            {
                moves.Add(new TaskMove(task.Id, from));
            }
        }
        return moves;
    }

    public Task SetDoneAsync(TaskEntry task, bool isDone, Guid userId) =>
        store.SetDoneAsync(task.Id, isDone, userId);

    public Task DeleteAsync(TaskEntry task) => store.DeleteAsync(task);

    // Clearing finished work is a delete, same as emptying the cart: the row is tombstoned
    // so every other phone drops it too.
    public async Task<int> ClearDoneAsync(Guid taskListId)
    {
        var done = TaskView.Tasks(store, taskListId).Where(t => t.IsDone).ToList();
        foreach (var task in done)
        {
            await store.DeleteAsync(task);
        }
        return done.Count;
    }

    public async Task<TaskList> CreateListAsync(string name)
    {
        var list = new TaskList { Id = Guid.NewGuid(), Name = name.Trim() };
        await store.SaveAsync(list);
        return list;
    }

    public Task RenameListAsync(TaskList list, string name)
    {
        list.Name = name.Trim();
        return store.SaveAsync(list);
    }

    // The last list can't go: the screen would have nowhere to put anything.
    public async Task<bool> DeleteListAsync(TaskList list)
    {
        if (TaskView.Lists(store).Count() <= 1)
        {
            return false;
        }
        foreach (var task in TaskView.Tasks(store, list.Id).ToList())
        {
            await store.DeleteAsync(task);
        }
        await store.DeleteAsync(list);
        return true;
    }
}
