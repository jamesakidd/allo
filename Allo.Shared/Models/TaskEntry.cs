namespace Allo.Shared.Models;

// Two field groups, resolved independently on sync, mirroring ListEntry:
//   content: TaskListId, Title, Priority, DueOn, Note (UpdatedAt/UpdatedBy)
//   done:    IsDone (DoneAt/DoneBy)
// So reprioritising a task while someone else ticks it off keeps both changes.
public class TaskEntry : SyncEntity
{
    public Guid Id { get; set; }
    public Guid TaskListId { get; set; }
    // Free text, not a catalog lookup. Tasks are one-offs; there is nothing to learn from
    // "call the plumber" the way there is from "milk".
    public string Title { get; set; } = "";
    public Priority Priority { get; set; } = Priority.Normal;
    // A date, not a timestamp. Everything else here is stored UTC, and an evening due-time
    // in UTC renders as the previous day depending on where you are — a household task has
    // no business carrying a timezone. It also maps straight to an all-day calendar event.
    public DateOnly? DueOn { get; set; }
    public string? Note { get; set; }
    public Guid AddedBy { get; set; }

    public bool IsDone { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public Guid? DoneBy { get; set; }
}
