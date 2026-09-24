using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Shared.Lists;

public sealed record TaskGroup(Priority Priority, IReadOnlyList<TaskEntry> Tasks);

// What's still to do, grouped by priority, and below it what's finished. Same shape as the
// shopping list's "In the cart": done work stays reviewable without being in the way.
public sealed record TaskSections(IReadOnlyList<TaskGroup> ToDo, IReadOnlyList<TaskEntry> Done)
{
    public int DoneCount => Done.Count;

    public bool IsEmpty => ToDo.Count == 0 && Done.Count == 0;
}

// Turns the device's rows into what the tasks screen shows. Priority does here what
// category does on the shopping list: it is the only thing that groups.
public static class TaskView
{
    public static TaskSections Build(LocalStore store, Guid taskListId, DateOnly today)
    {
        var tasks = Tasks(store, taskListId).ToList();
        return new TaskSections(
            Group(tasks.Where(t => !t.IsDone)),
            [.. tasks.Where(t => t.IsDone).OrderByDescending(t => t.DoneAt ?? DateTimeOffset.MinValue)]);

        IReadOnlyList<TaskGroup> Group(IEnumerable<TaskEntry> source) => source
            .GroupBy(t => t.Priority)
            .Select(g => new TaskGroup(g.Key, [.. g
                // Soonest due first; anything undated sorts after everything dated, so a
                // task with no deadline never jumps ahead of one with a real one.
                .OrderBy(t => t.DueOn ?? DateOnly.MaxValue)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)]))
            .OrderBy(g => g.Priority.Rank())
            .ToList();
    }

    public static IEnumerable<TaskEntry> Tasks(LocalStore store, Guid taskListId) =>
        store.Tasks.Where(t => !t.IsDeleted && t.TaskListId == taskListId);

    public static IEnumerable<TaskList> Lists(LocalStore store) =>
        store.TaskLists.Where(l => !l.IsDeleted).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase);

    // A task with no due date is never overdue, and neither is one already done.
    public static bool IsOverdue(TaskEntry task, DateOnly today) =>
        !task.IsDone && task.DueOn is { } due && due < today;

    public static bool IsDueToday(TaskEntry task, DateOnly today) =>
        !task.IsDone && task.DueOn == today;

    // "Overdue", "Today", "Tomorrow", then a plain date. Kept here rather than in the page
    // so the wording is testable and the same everywhere it appears.
    public static string? DueLabel(TaskEntry task, DateOnly today)
    {
        if (task.DueOn is not { } due)
        {
            return null;
        }
        var days = due.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => days == -1 ? "Yesterday" : $"Overdue · {due:MMM d}",
            0 => "Today",
            1 => "Tomorrow",
            < 7 => due.ToString("dddd"),
            _ => due.ToString("MMM d"),
        };
    }
}
