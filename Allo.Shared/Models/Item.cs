namespace Allo.Shared.Models;

// The reusable catalog, separate from list entries.
public class Item : SyncEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public Guid DefaultCategoryId { get; set; }
    public Unit DefaultUnit { get; set; } = Unit.Each;
    public List<string> Aliases { get; set; } = [];
    public List<string> DefaultTags { get; set; } = [];
    public string? Notes { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    // Times added to a list; with LastUsedAt, ranks autocomplete so staples surface first.
    public int UseCount { get; set; }

    // A non-default unit chosen on the last add. Chosen again on the next add, it becomes
    // DefaultUnit, so one odd purchase doesn't flip the default.
    public Unit? PendingUnit { get; set; }
}
