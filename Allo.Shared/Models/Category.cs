namespace Allo.Shared.Models;

public class Category : SyncEntity
{
    // Well-known id so every client can find it without a lookup. Always sorts last.
    public static readonly Guid UncategorizedId = new("00000000-0000-0000-0000-00000000c0ff");

    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }

    // Default walking order, relative to siblings. Used when no store is selected, and
    // copied into StoreCategoryOrder when a store is created.
    public int SortOrder { get; set; }
}
