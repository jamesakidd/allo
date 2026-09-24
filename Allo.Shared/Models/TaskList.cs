namespace Allo.Shared.Models;

// A named list of tasks. Its own table rather than a kind of ShoppingList: the two share
// only an id and a name, and a discriminator would make every existing list query
// responsible for filtering, which is how one screen ends up showing another's rows.
public class TaskList : SyncEntity
{
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-0000000022a1");

    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
