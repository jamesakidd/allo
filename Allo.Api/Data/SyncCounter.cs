namespace Allo.Api.Data;

// Single row (Id = 1) holding the last sequence number handed out.
public class SyncCounter
{
    public int Id { get; set; }
    public long Value { get; set; }
}
