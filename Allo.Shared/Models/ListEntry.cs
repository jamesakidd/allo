namespace Allo.Shared.Models;

// Two field groups, resolved independently on sync:
//   content: Quantity, Unit, Note, CategoryId, StoreId, Tags (UpdatedAt/UpdatedBy)
//   checked: IsChecked (CheckedAt/CheckedBy)
public class ListEntry : SyncEntity
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public Guid ItemId { get; set; }
    public Guid CategoryId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public Unit Unit { get; set; } = Unit.Each;
    public string? Note { get; set; }
    public Guid? StoreId { get; set; }
    public List<string> Tags { get; set; } = [];
    public Guid AddedBy { get; set; }

    public bool IsChecked { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public Guid? CheckedBy { get; set; }
}
