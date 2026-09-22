namespace Allo.Shared.Models;

public class Store : SyncEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
