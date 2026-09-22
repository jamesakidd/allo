using Allo.Client.Auth;
using Allo.Shared.Sync;
using Microsoft.JSInterop;

namespace Allo.Client.Sync;

// Decides when to sync: on start and login, when the app comes back to the foreground,
// when the network returns, and about a second after the user stops making changes.
public sealed class SyncCoordinator(LocalStore store, SyncEngine engine, AlloAuthStateProvider auth, IJSRuntime js)
    : IAsyncDisposable
{
    private static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(1);

    private DotNetObjectReference<SyncCoordinator>? _self;
    private CancellationTokenSource? _pendingPush;

    public async Task StartAsync()
    {
        if (_self is not null)
        {
            return;
        }
        _self = DotNetObjectReference.Create(this);
        store.LocalChangeQueued += SchedulePush;
        engine.StateChanged += OnEngineStateChanged;
        auth.AuthenticationStateChanged += _ => SyncIfLoggedIn();
        await js.InvokeVoidAsync("alloSync.register", _self);
        // Wait for the first "who's logged in" answer (possibly the cached user, offline),
        // or the first sync would be skipped as logged out.
        await auth.GetAuthenticationStateAsync();
        SyncIfLoggedIn();
    }

    [JSInvokable]
    public void OnWake() => SyncIfLoggedIn();

    // An account still on its temporary password is refused by the server until it sets a
    // real one, so don't sync it: the queue would only collect 403s and show a sync error
    // on top of the change-your-password screen.
    public Task SyncNowAsync() =>
        auth.User is null or { MustChangePassword: true } ? Task.CompletedTask : engine.SyncAsync();

    private void SyncIfLoggedIn() => _ = SyncNowAsync();

    // Debounced: a burst of taps becomes one push.
    private void SchedulePush()
    {
        _pendingPush?.Cancel();
        var cancel = _pendingPush = new CancellationTokenSource();
        _ = Task.Delay(PushDelay, cancel.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                SyncIfLoggedIn();
            }
        }, TaskScheduler.Default);
    }

    // The login cookie expired: send the user to log in. The queue stays on the device
    // and goes up after the next login.
    private void OnEngineStateChanged()
    {
        if (engine.State == SyncState.NeedsLogin && auth.User is not null)
        {
            _ = auth.SetUserAsync(null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        store.LocalChangeQueued -= SchedulePush;
        engine.StateChanged -= OnEngineStateChanged;
        _pendingPush?.Cancel();
        _self?.Dispose();
        await Task.CompletedTask;
    }
}
