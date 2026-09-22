namespace Allo.Shared.Models;

public class Store : SyncEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    // Hex like "#66bb6a", used for the pill on list rows. Null falls back to the theme.
    public string? Color { get; set; }
}
