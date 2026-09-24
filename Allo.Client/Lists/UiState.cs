using Allo.Shared.Models;
using Allo.Shared.Sync;
using Blazored.LocalStorage;

namespace Allo.Client.Lists;

// Which list and store the shopper is looking at. Per device, not synced: two people in
// different stores shouldn't move each other's view.
public sealed class UiState(ILocalStorageService storage, LocalStore store)
{
    private const string ListKey = "allo.ui.listId";
    private const string StoreKey = "allo.ui.storeId";
    private const string TaskListKey = "allo.ui.taskListId";

    public Guid ListId { get; private set; } = ShoppingList.DefaultId;

    // null means "any store": the whole list, in the default category order.
    public Guid? StoreId { get; private set; }

    public Guid TaskListId { get; private set; } = TaskList.DefaultId;

    public event Action? Changed;

    public async Task LoadAsync()
    {
        ListId = await TryGetAsync<Guid>(ListKey) is { } listId && listId != Guid.Empty ? listId : ListId;
        StoreId = await TryGetAsync<Guid>(StoreKey) is { } storeId && storeId != Guid.Empty ? storeId : null;
        TaskListId = await TryGetAsync<Guid>(TaskListKey) is { } taskListId && taskListId != Guid.Empty
            ? taskListId : TaskListId;
        FallBackIfMissing();
        Changed?.Invoke();
    }

    public async Task SetListAsync(Guid listId)
    {
        ListId = listId;
        await TrySetAsync(ListKey, listId);
        Changed?.Invoke();
    }

    public async Task SetStoreAsync(Guid? storeId)
    {
        StoreId = storeId;
        await TrySetAsync(StoreKey, storeId ?? Guid.Empty);
        Changed?.Invoke();
    }

    public async Task SetTaskListAsync(Guid taskListId)
    {
        TaskListId = taskListId;
        await TrySetAsync(TaskListKey, taskListId);
        Changed?.Invoke();
    }

    // A list or store deleted on another phone shouldn't leave this one showing nothing.
    public void FallBackIfMissing()
    {
        if (!store.Lists.Any(l => l.Id == ListId && !l.IsDeleted))
        {
            ListId = store.Lists.Where(l => !l.IsDeleted).OrderBy(l => l.Name).FirstOrDefault()?.Id
                ?? ShoppingList.DefaultId;
        }
        if (StoreId is { } id && !store.Stores.Any(s => s.Id == id && !s.IsDeleted))
        {
            StoreId = null;
        }
        if (!store.TaskLists.Any(l => l.Id == TaskListId && !l.IsDeleted))
        {
            TaskListId = store.TaskLists.Where(l => !l.IsDeleted).OrderBy(l => l.Name).FirstOrDefault()?.Id
                ?? TaskList.DefaultId;
        }
    }

    private async Task<T?> TryGetAsync<T>(string key) where T : struct
    {
        try
        {
            return await storage.GetItemAsync<T?>(key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task TrySetAsync<T>(string key, T value)
    {
        try
        {
            await storage.SetItemAsync(key, value);
        }
        catch (Exception)
        {
        }
    }
}
