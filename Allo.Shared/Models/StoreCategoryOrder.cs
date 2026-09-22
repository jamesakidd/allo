namespace Allo.Shared.Models;

// Keyed on (StoreId, CategoryId) rather than its own id, so two devices setting the
// same store's order offline update the same row instead of creating duplicates.
public class StoreCategoryOrder : SyncEntity
{
    public Guid StoreId { get; set; }
    public Guid CategoryId { get; set; }
    public int SortOrder { get; set; }
}
