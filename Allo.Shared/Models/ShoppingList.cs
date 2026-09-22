namespace Allo.Shared.Models;

public class ShoppingList : SyncEntity
{
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-0000000011a1");

    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
