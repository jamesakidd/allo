using System.Net.Http.Json;
using System.Text.Json;
using Allo.Api.Data;
using Allo.Shared.Auth;
using Allo.Shared.Models;
using Allo.Shared.Sync;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace Allo.Tests;

// Stores JSON strings like browser local storage does, so a "reload" really re-reads.
public sealed class InMemoryStorage : ISyncStorage
{
    private readonly Dictionary<string, string> _values = new();

    public Task<T?> GetAsync<T>(string key) =>
        Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

    public Task SetAsync<T>(string key, T value)
    {
        _values[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }
}

// Airplane mode: while Offline, every request fails the way a dead network does.
public sealed class NetworkSwitch : DelegatingHandler
{
    public bool Offline { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Offline
            ? throw new HttpRequestException("Network is unreachable.")
            : base.SendAsync(request, cancellationToken);
}

// A family member's phone: its own cookie, network switch, local storage and engine.
public sealed class Phone
{
    public required HttpClient Http { get; init; }
    public required NetworkSwitch Network { get; init; }
    public required InMemoryStorage Storage { get; init; }
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public LocalStore Store { get; private set; } = null!;
    public SyncEngine Engine { get; private set; } = null!;
    public Func<Task>? BeforePushResponse { get; set; }

    public static async Task<Phone> CreateAsync(TestApp app, string username, string password)
    {
        var network = new NetworkSwitch();
        var http = app.Factory.CreateDefaultClient(network, new CookieContainerHandler());
        var login = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        login.EnsureSuccessStatusCode();
        var user = (await login.Content.ReadFromJsonAsync<CurrentUser>())!;
        if (user.MustChangePassword)
        {
            // A real first login gets the temporary password out of the way before the
            // account can do anything, so the phone does too.
            (await http.PostAsJsonAsync("/api/auth/password",
                new ChangePasswordRequest(password, TestApp.SettledPassword))).EnsureSuccessStatusCode();
            password = TestApp.SettledPassword;
        }
        var phone = new Phone
        {
            Http = http, Network = network, Storage = new InMemoryStorage(),
            UserId = user.Id, Username = username, Password = password,
        };
        await phone.ReloadAsync();
        return phone;
    }

    // Closing and reopening the app: everything in memory is rebuilt from storage.
    public async Task ReloadAsync()
    {
        Store = new LocalStore(Storage, TimeProvider.System);
        await Store.LoadAsync();
        Engine = new SyncEngine(Store, new HookedTransport(new HttpSyncTransport(Http), this));
    }

    public Task SyncAsync() => Engine.SyncAsync();

    public ListEntry Entry(Guid id) => Store.Entries.Single(e => e.Id == id);

    public async Task<ListEntry> AddEntryAsync(string itemName, decimal quantity = 1)
    {
        var entry = new ListEntry
        {
            Id = Guid.NewGuid(),
            ListId = ShoppingList.DefaultId,
            ItemId = CatalogSeeder.NameBasedId(itemName),
            CategoryId = Category.UncategorizedId,
            Quantity = quantity,
            AddedBy = UserId,
        };
        await Store.SaveAsync(entry);
        return entry;
    }

    private sealed class HookedTransport(ISyncTransport inner, Phone phone) : ISyncTransport
    {
        public Task<SyncPullResponse> PullAsync(long since, CancellationToken cancellationToken = default) =>
            inner.PullAsync(since, cancellationToken);

        public async Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default)
        {
            var response = await inner.PushAsync(request, cancellationToken);
            if (phone.BeforePushResponse is { } hook)
            {
                await hook();
            }
            return response;
        }
    }
}

public static class TestAppExtensions
{
    // Adds a family member through the admin and returns their phone.
    public static async Task<Phone> AddPhoneAsync(this TestApp app, string username)
    {
        var admin = await LoggedInAdminAsync(app);
        const string password = "family-password";
        if (username == TestApp.AdminUsername)
        {
            return await Phone.CreateAsync(app, username, TestApp.SettledPassword);
        }
        (await admin.PostAsJsonAsync("/api/users",
            new AddFamilyMemberRequest(username, username, password))).EnsureSuccessStatusCode();
        return await Phone.CreateAsync(app, username, password);
    }

    // The bootstrap account is on a temporary password, and the API refuses everything
    // else until it changes — so settle it first, like a person would. Callable more than
    // once per app, since the second phone finds the password already changed.
    private static async Task<HttpClient> LoggedInAdminAsync(TestApp app)
    {
        var admin = app.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(TestApp.AdminUsername, TestApp.AdminPassword));
        if (!login.IsSuccessStatusCode)
        {
            login = await admin.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(TestApp.AdminUsername, TestApp.SettledPassword));
        }
        login.EnsureSuccessStatusCode();
        if ((await login.Content.ReadFromJsonAsync<CurrentUser>())!.MustChangePassword)
        {
            (await admin.PostAsJsonAsync("/api/auth/password",
                new ChangePasswordRequest(TestApp.AdminPassword, TestApp.SettledPassword))).EnsureSuccessStatusCode();
        }
        return admin;
    }
}
