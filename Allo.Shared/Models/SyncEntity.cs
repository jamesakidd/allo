namespace Allo.Shared.Models;

// Base for every table that syncs to clients. Sequence is the only sync cursor and is
// assigned by the server on every accepted change; UpdatedAt is informational only.
public abstract class SyncEntity
{
    public long Sequence { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
