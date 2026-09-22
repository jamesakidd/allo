using System.Security.Claims;
using Allo.Api.Data;
using Allo.Shared.Auth;
using Allo.Shared.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Auth;

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        // Cast: a lone HttpContext parameter would otherwise bind as a RequestDelegate and drop the result.
        auth.MapPost("/logout", (Delegate)Logout).AllowAnonymous();
        auth.MapGet("/me", Me);
        auth.MapPost("/password", ChangePassword);
        auth.MapPut("/display-name", UpdateDisplayName);

        var users = api.MapGroup("/users");
        users.MapGet("/", ListFamily);
        users.MapPost("/", AddFamilyMember);
    }

    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> Login(LoginRequest request, AppDbContext db,
        IPasswordHasher<UserLogin> hasher, HttpContext http)
    {
        var username = UsernameRules.Normalize(request.Username ?? "");
        var login = await db.UserLogins.SingleOrDefaultAsync(l => l.Username == username);

        // Verify against a dummy hash for unknown usernames so response time doesn't reveal
        // which usernames exist.
        var result = hasher.VerifyHashedPassword(login ?? DummyLogin, login?.PasswordHash ?? DummyHash,
            request.Password ?? "");
        if (login is null || result == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            login.PasswordHash = hasher.HashPassword(login, request.Password!);
            await db.SaveChangesAsync();
        }

        var user = await db.Users.SingleAsync(u => u.Id == login.UserId);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, login.Username)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        // Persistent, so the cookie survives the browser (and the installed PWA) closing.
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return Results.Ok(ToCurrentUser(user, login));
    }

    private static async Task<IResult> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(ClaimsPrincipal principal, AppDbContext db)
    {
        var (user, login) = await Load(db, principal.UserId());
        return Results.Ok(ToCurrentUser(user, login));
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, ClaimsPrincipal principal,
        AppDbContext db, IPasswordHasher<UserLogin> hasher)
    {
        var (_, login) = await Load(db, principal.UserId());
        if (hasher.VerifyHashedPassword(login, login.PasswordHash, request.CurrentPassword ?? "")
            == PasswordVerificationResult.Failed)
        {
            return Invalid(nameof(request.CurrentPassword), "Current password is incorrect.");
        }
        if (PasswordRules.Validate(request.NewPassword) is { } error)
        {
            return Invalid(nameof(request.NewPassword), error);
        }

        login.PasswordHash = hasher.HashPassword(login, request.NewPassword);
        login.MustChangePassword = false;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateDisplayName(UpdateDisplayNameRequest request, ClaimsPrincipal principal,
        AppDbContext db)
    {
        if (ValidateDisplayName(request.DisplayName) is { } error)
        {
            return Invalid(nameof(request.DisplayName), error);
        }

        var (user, login) = await Load(db, principal.UserId());
        user.DisplayName = request.DisplayName.Trim();
        await db.SaveChangesAsync();
        return Results.Ok(ToCurrentUser(user, login));
    }

    private static async Task<IResult> ListFamily(AppDbContext db)
    {
        var members = await db.UserLogins
            .Join(db.Users, l => l.UserId, u => u.Id, (l, u) => new FamilyMember(u.Id, l.Username, u.DisplayName))
            .ToListAsync();
        return Results.Ok(members.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase));
    }

    // No roles: any family member can add another. The new account starts with a
    // temporary password that must be changed at first login.
    private static async Task<IResult> AddFamilyMember(AddFamilyMemberRequest request, AppDbContext db,
        IPasswordHasher<UserLogin> hasher)
    {
        var errors = new Dictionary<string, string[]>();
        if (UsernameRules.Validate(request.Username) is { } usernameError)
        {
            errors[nameof(request.Username)] = [usernameError];
        }
        if (ValidateDisplayName(request.DisplayName) is { } displayNameError)
        {
            errors[nameof(request.DisplayName)] = [displayNameError];
        }
        if (PasswordRules.Validate(request.TemporaryPassword) is { } passwordError)
        {
            errors[nameof(request.TemporaryPassword)] = [passwordError];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var username = UsernameRules.Normalize(request.Username);
        if (await db.UserLogins.AnyAsync(l => l.Username == username))
        {
            return Invalid(nameof(request.Username), "That username is taken.");
        }

        var (user, _) = await CreateAccount(db, hasher, username, request.DisplayName.Trim(),
            request.TemporaryPassword, mustChangePassword: true);
        return Results.Created($"/api/users/{user.Id}", new FamilyMember(user.Id, username, user.DisplayName));
    }

    public static async Task<(User, UserLogin)> CreateAccount(AppDbContext db, IPasswordHasher<UserLogin> hasher,
        string username, string displayName, string password, bool mustChangePassword)
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = displayName };
        var login = new UserLogin
        {
            UserId = user.Id,
            Username = UsernameRules.Normalize(username),
            MustChangePassword = mustChangePassword,
        };
        login.PasswordHash = hasher.HashPassword(login, password);
        db.AddRange(user, login);
        await db.SaveChangesAsync();
        return (user, login);
    }

    private static string? ValidateDisplayName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "Display name is required."
        : displayName.Trim().Length > 100 ? "Display name must be at most 100 characters."
        : null;

    private static async Task<(User, UserLogin)> Load(AppDbContext db, Guid userId) =>
        (await db.Users.SingleAsync(u => u.Id == userId), await db.UserLogins.SingleAsync(l => l.UserId == userId));

    private static CurrentUser ToCurrentUser(User user, UserLogin login) =>
        new(user.Id, login.Username, user.DisplayName, login.MustChangePassword);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static readonly UserLogin DummyLogin = new();
    private static readonly string DummyHash = new PasswordHasher<UserLogin>().HashPassword(DummyLogin, "not-a-real-password");
}
