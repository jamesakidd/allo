namespace Allo.Api.Data;

// Credentials, kept server-side only and apart from the shared User model so a password
// hash can never end up in a payload sent to the client.
public class UserLogin
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool MustChangePassword { get; set; }
}
