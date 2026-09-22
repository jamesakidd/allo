using Allo.Api.Data;
using Allo.Shared.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Auth;

// Creates the first account from configuration (Admin__Username, Admin__InitialPassword,
// optional Admin__DisplayName) when no accounts exist yet. After that, family members are
// added in the app. The initial password must be changed at first login.
public static class AdminBootstrap
{
    public static async Task RunAsync(AppDbContext db, IPasswordHasher<UserLogin> hasher, IConfiguration config,
        ILogger logger)
    {
        if (await db.UserLogins.AnyAsync())
        {
            return;
        }

        var username = config["Admin:Username"];
        var password = config["Admin:InitialPassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning("No accounts exist. Set Admin__Username and Admin__InitialPassword to create the first one.");
            return;
        }

        if (PasswordRules.Validate(password) is { } error)
        {
            logger.LogError("Admin__InitialPassword rejected: {Error}", error);
            return;
        }

        var displayName = config["Admin:DisplayName"] is { Length: > 0 } name ? name : username.Trim();
        await AuthEndpoints.CreateAccount(db, hasher, username, displayName, password, mustChangePassword: true);
        logger.LogInformation("Created the first account, {Username}.", username.Trim());
    }
}
