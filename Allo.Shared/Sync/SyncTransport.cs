using System.Net;
using System.Net.Http.Json;

namespace Allo.Shared.Sync;

public interface ISyncTransport
{
    Task<SyncPullResponse> PullAsync(long since, CancellationToken cancellationToken = default);
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default);
}

// The login cookie is missing or expired. Queued changes stay put until the next login.
public sealed class SyncUnauthorizedException() : Exception("Not logged in.");

public sealed class HttpSyncTransport(HttpClient http) : ISyncTransport
{
    public async Task<SyncPullResponse> PullAsync(long since, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/sync?since={since}", cancellationToken);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<SyncPullResponse>(cancellationToken))!;
    }

    public async Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync("api/sync", request, cancellationToken);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<SyncPushResponse>(cancellationToken))!;
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SyncUnauthorizedException();
        }
        response.EnsureSuccessStatusCode();
        return Task.CompletedTask;
    }
}
