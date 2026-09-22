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
}
