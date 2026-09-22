using System.Net;
using System.Net.Http.Json;
using Allo.Shared.Auth;

namespace Allo.Tests;

public class AuthTests : IDisposable
{
    private readonly TestApp _app = new();

    private static Task<HttpResponseMessage> Login(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));

    private async Task<HttpClient> LoggedInAdmin()
    {
        var client = _app.CreateClient();
        (await Login(client, TestApp.AdminUsername, TestApp.AdminPassword)).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task ApiRequiresLogin()
    {
        var client = _app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task BootstrapAdmin_CanLogIn_AndMustChangePassword()
    {
        var client = _app.CreateClient();

        var response = await Login(client, " Admin ", TestApp.AdminPassword);

        response.EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me");
        Assert.Equal("admin", me!.Username);
        Assert.Equal("Sam", me.DisplayName);
        Assert.True(me.MustChangePassword);
    }

    [Fact]
    public async Task Login_SetsAPersistentCookie()
    {
        var client = _app.CreateClient();

        var response = await Login(client, TestApp.AdminUsername, TestApp.AdminPassword);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("AlloAuth=", cookie);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("admin", "wrong-password")]
    [InlineData("nobody", "initial-pass")]
    public async Task Login_Rejects_BadCredentials(string username, string password)
    {
        var client = _app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, username, password)).StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        var client = _app.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "admin", "wrong")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client, "admin", "wrong")).StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var client = await LoggedInAdmin();

        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ClearsTheFlag_AndTheNewPasswordWorks()
    {
        var client = await LoggedInAdmin();

        var response = await client.PostAsJsonAsync("/api/auth/password",
            new ChangePasswordRequest(TestApp.AdminPassword, "a-new-password"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False((await client.GetFromJsonAsync<CurrentUser>("/api/auth/me"))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await Login(_app.CreateClient(), "admin", "a-new-password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(_app.CreateClient(), "admin", TestApp.AdminPassword)).StatusCode);
    }

    [Theory]
    [InlineData("wrong-current", "a-new-password")]
    [InlineData(TestApp.AdminPassword, "short")]
    public async Task ChangePassword_Rejects_WrongCurrentOrWeakNew(string current, string next)
    {
        var client = await LoggedInAdmin();

        var response = await client.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(current, next));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDisplayName_Trims_AndRejectsBlank()
    {
        var client = await LoggedInAdmin();

        var ok = await client.PutAsJsonAsync("/api/auth/display-name", new UpdateDisplayNameRequest("  Dad "));
        var blank = await client.PutAsJsonAsync("/api/auth/display-name", new UpdateDisplayNameRequest("  "));

        Assert.Equal("Dad", (await ok.Content.ReadFromJsonAsync<CurrentUser>())!.DisplayName);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task AddFamilyMember_CanLogIn_AndMustChangePassword()
    {
        var admin = await LoggedInAdmin();

        var response = await admin.PostAsJsonAsync("/api/users",
            new AddFamilyMemberRequest("Alex", "Alex", "temp-password"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var alex = _app.CreateClient();
        (await Login(alex, "alex", "temp-password")).EnsureSuccessStatusCode();
        Assert.True((await alex.GetFromJsonAsync<CurrentUser>("/api/auth/me"))!.MustChangePassword);
        var family = await admin.GetFromJsonAsync<List<FamilyMember>>("/api/users");
        Assert.Equal(["Alex", "Sam"], family!.Select(m => m.DisplayName));
    }

    [Theory]
    [InlineData("admin", "Someone", "temp-password")]
    [InlineData("two words", "Someone", "temp-password")]
    [InlineData("kid", "", "temp-password")]
    [InlineData("kid", "Kid", "short")]
    public async Task AddFamilyMember_Rejects_TakenOrInvalid(string username, string displayName, string password)
    {
        var admin = await LoggedInAdmin();

        var response = await admin.PostAsJsonAsync("/api/users",
            new AddFamilyMemberRequest(username, displayName, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose() => _app.Dispose();
}
