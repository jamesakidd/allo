namespace Allo.Shared.Models;

// Minimal for now; the Auth phase adds username and password hash.
public class User
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
}
