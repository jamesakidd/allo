namespace Allo.Shared.Auth;

public static class PasswordRules
{
    public const int MinLength = 8;

    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength
            ? $"Password must be at least {MinLength} characters."
            : null;
}

public static class UsernameRules
{
    public const int MaxLength = 50;

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public static string? Validate(string? username)
    {
        var normalized = Normalize(username ?? "");
        if (normalized.Length == 0)
        {
            return "Username is required.";
        }
        if (normalized.Length > MaxLength)
        {
            return $"Username must be at most {MaxLength} characters.";
        }
        return normalized.Any(char.IsWhiteSpace) ? "Username cannot contain spaces." : null;
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record CurrentUser(Guid Id, string Username, string DisplayName, bool MustChangePassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateDisplayNameRequest(string DisplayName);

public sealed record AddFamilyMemberRequest(string Username, string DisplayName, string TemporaryPassword);

public sealed record FamilyMember(Guid Id, string Username, string DisplayName);
