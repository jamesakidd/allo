namespace Allo.Shared.Sync;

public enum SyncState
{
    Idle,
    Syncing,
    // The server couldn't be reached. Changes stay queued; the next trigger retries.
    Offline,
    // The login expired. Changes stay queued until the next login.
    NeedsLogin,
    // The server answered with an error.
    Error,
}

// One sync cycle: push the queue, then pull everything newer than the cursor. Pushing
// first means a pull never has to overwrite a local change the server hasn't seen.
// Only one cycle runs at a time; a request during a cycle runs one more cycle after it.
public sealed class SyncEngine(LocalStore store, ISyncTransport transport)
{
    private Task? _running;
    private bool _again;

    public SyncState State { get; private set; } = SyncState.Idle;

    public event Action? StateChanged;

    public Task SyncAsync()
    {
        if (_running is { IsCompleted: false })
        {
            _again = true;
            return _running;
        }
        return _running = RunAsync();
    }

    private async Task RunAsync()
    {
        do
        {
            _again = false;
            await CycleAsync();
        }
        while (_again && State == SyncState.Idle);
    }

    private async Task CycleAsync()
    {
        SetState(SyncState.Syncing);
        try
        {
            var snapshot = store.TakeSnapshot();
            if (!snapshot.Request.IsEmpty)
            {
                var pushed = await transport.PushAsync(snapshot.Request);
                await store.ApplyPushResponseAsync(snapshot, pushed);
            }
            await store.ApplyPullAsync(await transport.PullAsync(store.Cursor));
            SetState(SyncState.Idle);
        }
        catch (SyncUnauthorizedException)
        {
            SetState(SyncState.NeedsLogin);
        }
        catch (HttpRequestException e) when (e.StatusCode is null)
        {
            SetState(SyncState.Offline);
        }
        catch (TaskCanceledException)
        {
            SetState(SyncState.Offline);
        }
        catch (HttpRequestException)
        {
            SetState(SyncState.Error);
        }
    }

    private void SetState(SyncState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}
