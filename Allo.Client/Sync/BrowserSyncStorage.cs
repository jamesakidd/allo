using Allo.Shared.Sync;
using Blazored.LocalStorage;

namespace Allo.Client.Sync;

public sealed class BrowserSyncStorage(ILocalStorageService localStorage) : ISyncStorage
{
    public async Task<T?> GetAsync<T>(string key) => await localStorage.GetItemAsync<T>(key);

    public async Task SetAsync<T>(string key, T value) => await localStorage.SetItemAsync(key, value);
}
