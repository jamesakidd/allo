using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Allo.Shared.Auth;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace Allo.Client.Auth;

// Who is logged in. Asks the server, but remembers the last answer on the device so the
// app still opens as the right person with no signal. Only an actual 401 logs out.
public sealed class AlloAuthStateProvider(HttpClient http, ILocalStorageService storage) : AuthenticationStateProvider
{
    private const string CacheKey = "allo.currentUser";

    private Task? _initialLoad;

    public CurrentUser? User { get; private set; }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await (_initialLoad ??= RefreshAsync());
        return ToState(User);
    }

    // Returns an error message to show, or null on success.
    public async Task<string?> LoginAsync(string username, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));
        }
        catch (HttpRequestException)
        {
            return "Can't reach the server. Check your connection and try again.";
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                await SetUserAsync(await response.Content.ReadFromJsonAsync<CurrentUser>());
                return null;
            case HttpStatusCode.Unauthorized:
                return "Wrong username or password.";
            case HttpStatusCode.TooManyRequests:
                return "Too many attempts. Wait a minute and try again.";
            default:
                return "Something went wrong. Try again.";
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await http.PostAsync("api/auth/logout", null);
        }
        catch (HttpRequestException)
        {
            // Offline: the cookie outlives this, but the device forgets who was logged in.
        }
        await SetUserAsync(null);
    }

    public async Task SetUserAsync(CurrentUser? user)
    {
        User = user;
        await TryStorage(() => user is null
            ? storage.RemoveItemAsync(CacheKey).AsTask()
            : storage.SetItemAsync(CacheKey, user).AsTask());
        NotifyAuthenticationStateChanged(Task.FromResult(ToState(user)));
    }

    private async Task RefreshAsync()
    {
        try
        {
            var response = await http.GetAsync("api/auth/me");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await SetUserAsync(null);
                return;
            }
            if (response.IsSuccessStatusCode)
            {
                await SetUserAsync(await response.Content.ReadFromJsonAsync<CurrentUser>());
                return;
            }
        }
        catch (HttpRequestException)
        {
            // Offline: fall through to the cached user.
        }

        await TryStorage(async () => User = await storage.GetItemAsync<CurrentUser>(CacheKey));
    }

    // Local storage can be unavailable (private browsing, blocked site data). The app
    // still works; it just can't open offline without a fresh login.
    private static async Task TryStorage(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
        }
    }

    private static AuthenticationState ToState(CurrentUser? user) => new(user is null
        ? new ClaimsPrincipal(new ClaimsIdentity())
        : new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.DisplayName)],
            "Allo")));
}
