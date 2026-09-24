using System.Text.Json;
using Allo.Shared.Auth;
using Allo.Shared.Models;

namespace Allo.Shared.Sync;

public interface ISyncStorage
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
}

// The device's copy of every synced table, plus the queue of changes not yet accepted by
// the server. Every user action writes here and is visible at once; the network is never
// in the way. The queue survives closing the app because it's persisted with the data.
//
// The queue holds markers ("this row's content changed", "this entry's checked state
// changed"), not copies. A push sends the row's current values, so ten edits to the same
// row offline become one change on the wire.
public sealed class LocalStore(ISyncStorage storage, TimeProvider time)
{
    private const string Prefix = "allo.sync.";
    private const string Content = "content";
    private const string Checked = "checked";
    private const string Done = "done";

    private readonly Table<Category> _categories = new(SyncTable.Category, "categories", c => c.Id.ToString());
    private readonly Table<Store> _stores = new(SyncTable.Store, "stores", s => s.Id.ToString());
    private readonly Table<StoreCategoryOrder> _orders = new(SyncTable.StoreCategoryOrder, "storeCategoryOrders", SyncKeys.Of);
    private readonly Table<Item> _items = new(SyncTable.Item, "items", i => i.Id.ToString());
    private readonly Table<ShoppingList> _lists = new(SyncTable.ShoppingList, "lists", l => l.Id.ToString());
    private readonly Table<ListEntry> _entries = new(SyncTable.ListEntry, "entries", e => e.Id.ToString());
    private readonly Table<TaskList> _taskLists = new(SyncTable.TaskList, "taskLists", l => l.Id.ToString());
    private readonly Table<TaskEntry> _tasks = new(SyncTable.TaskEntry, "tasks", t => t.Id.ToString());
    private Meta _meta = new();
    private bool _metaDirty;

    // Anything changed, locally or from a sync. The UI re-renders on this.
    public event Action? Changed;

    // A local change was queued. Used to push soon after the user stops tapping.
    public event Action? LocalChangeQueued;

    // All rows including tombstones; filter IsDeleted for display.
    public IReadOnlyCollection<Category> Categories => _categories.Rows.Values;
    public IReadOnlyCollection<Store> Stores => _stores.Rows.Values;
    public IReadOnlyCollection<StoreCategoryOrder> StoreCategoryOrders => _orders.Rows.Values;
    public IReadOnlyCollection<Item> Items => _items.Rows.Values;
    public IReadOnlyCollection<ShoppingList> Lists => _lists.Rows.Values;
    public IReadOnlyCollection<ListEntry> Entries => _entries.Rows.Values;
    public IReadOnlyCollection<TaskList> TaskLists => _taskLists.Rows.Values;
    public IReadOnlyCollection<TaskEntry> Tasks => _tasks.Rows.Values;
    public IReadOnlyList<FamilyMember> Users => _meta.Users;

    public long Cursor => _meta.Cursor;
    public DateTimeOffset? LastSyncedAt => _meta.LastSyncedAt;
    public int PendingCount => _meta.Pending.Count;

    public async Task LoadAsync()
    {
        _meta = await storage.GetAsync<Meta>(Prefix + "meta") ?? new Meta();
        foreach (var table in AllTables)
        {
            await table.LoadAsync(storage, Prefix);
        }
        Changed?.Invoke();
    }

    // Saves a new or edited row: its content group (for ListEntry, everything except the
    // checked state), or a delete when IsDeleted is set.
    public Task SaveAsync<T>(T row) where T : SyncEntity
    {
        var table = TableFor<T>();
        row.UpdatedAt = time.GetUtcNow();
        table.Put(row);
        Mark(table, table.Key(row), Content);
        return AfterLocalChangeAsync();
    }

    public Task DeleteAsync<T>(T row) where T : SyncEntity
    {
        row.IsDeleted = true;
        return SaveAsync(row);
    }

    public Task SetCheckedAsync(Guid entryId, bool isChecked, Guid userId)
    {
        if (!_entries.Rows.TryGetValue(entryId.ToString(), out var entry))
        {
            throw new InvalidOperationException($"Unknown entry {entryId}.");
        }
        entry.IsChecked = isChecked;
        entry.CheckedAt = time.GetUtcNow();
        entry.CheckedBy = userId;
        _entries.Dirty = true;
        Mark(_entries, entry.Id.ToString(), Checked);
        return AfterLocalChangeAsync();
    }

    public Task SetDoneAsync(Guid taskId, bool isDone, Guid userId)
    {
        if (!_tasks.Rows.TryGetValue(taskId.ToString(), out var task))
        {
            throw new InvalidOperationException($"Unknown task {taskId}.");
        }
        task.IsDone = isDone;
        task.DoneAt = time.GetUtcNow();
        task.DoneBy = userId;
        _tasks.Dirty = true;
        Mark(_tasks, task.Id.ToString(), Done);
        return AfterLocalChangeAsync();
    }

    // What to push: current values of every marked row, with the revision each marker had,
    // so markers re-set by edits made during the push aren't cleared by its response.
    public PushSnapshot TakeSnapshot()
    {
        var request = new SyncPushRequest();
        foreach (var (marker, _) in _meta.Pending)
        {
            var (tableName, key, group) = ParseMarker(marker);
            switch (tableName)
            {
                case SyncTable.Category: AddContent(request.Rows.Categories, _categories, key); break;
                case SyncTable.Store: AddContent(request.Rows.Stores, _stores, key); break;
                case SyncTable.StoreCategoryOrder: AddContent(request.Rows.StoreCategoryOrders, _orders, key); break;
                case SyncTable.Item: AddContent(request.Rows.Items, _items, key); break;
                case SyncTable.ShoppingList: AddContent(request.Rows.Lists, _lists, key); break;
                case SyncTable.ListEntry when group == Checked:
                    if (_entries.Rows.TryGetValue(key, out var entry))
                    {
                        request.Checks.Add(new EntryCheck(entry.Id, entry.IsChecked, entry.CheckedAt ?? time.GetUtcNow()));
                    }
                    break;
                case SyncTable.ListEntry: AddContent(request.Rows.Entries, _entries, key); break;
                case SyncTable.TaskList: AddContent(request.Rows.TaskLists, _taskLists, key); break;
                case SyncTable.TaskEntry when group == Done:
                    if (_tasks.Rows.TryGetValue(key, out var task))
                    {
                        request.Dones.Add(new TaskDone(task.Id, task.IsDone, task.DoneAt ?? time.GetUtcNow()));
                    }
                    break;
                case SyncTable.TaskEntry: AddContent(request.Rows.Tasks, _tasks, key); break;
            }
        }
        return new PushSnapshot(request, new Dictionary<string, long>(_meta.Pending));
    }

    public async Task ApplyPushResponseAsync(PushSnapshot snapshot, SyncPushResponse response)
    {
        foreach (var (marker, revision) in snapshot.Revisions)
        {
            if (_meta.Pending.TryGetValue(marker, out var current) && current == revision)
            {
                _meta.Pending.Remove(marker);
            }
        }

        foreach (var remap in response.ItemRemaps)
        {
            // The server already had this item under another id; use that one.
            _items.Rows.Remove(remap.From.ToString());
            _items.Dirty = true;
            Unmark(SyncTable.Item, remap.From.ToString());
            foreach (var entry in _entries.Rows.Values.Where(e => e.ItemId == remap.From))
            {
                entry.ItemId = remap.To;
                _entries.Dirty = true;
            }
        }

        // A rejected change is dropped: take the server's version, or forget a row the
        // server never accepted.
        foreach (var rejection in response.Rejections)
        {
            Unmark(rejection.Table, rejection.Key);
            var table = AllTables.Single(t => t.Name == rejection.Table);
            if (table.TryGetSequence(rejection.Key) == 0)
            {
                table.Remove(rejection.Key);
            }
        }
        ApplyServerRows(response.Current, force: true);

        _metaDirty = true;
        await PersistAsync();
        Changed?.Invoke();
    }

    // Server rows win, except over a local change still waiting to be pushed: that keeps
    // its values until the server has accepted it. A tombstone always wins.
    public async Task ApplyPullAsync(SyncPullResponse pull)
    {
        ApplyServerRows(pull.Rows, force: false);
        _meta.Cursor = pull.Cursor;
        _meta.Users = pull.Users;
        _meta.LastSyncedAt = time.GetUtcNow();
        _metaDirty = true;
        await PersistAsync();
        Changed?.Invoke();
    }

    private void ApplyServerRows(SyncRows rows, bool force)
    {
        ApplyServerRows(_categories, rows.Categories, force);
        ApplyServerRows(_stores, rows.Stores, force);
        ApplyServerRows(_orders, rows.StoreCategoryOrders, force);
        ApplyServerRows(_items, rows.Items, force);
        ApplyServerRows(_lists, rows.Lists, force);

        ApplyServerRows(_taskLists, rows.TaskLists, force);
        ApplyTwoGroupRows(_entries, rows.Entries, force, Checked, CopyChecked);
        ApplyTwoGroupRows(_tasks, rows.Tasks, force, Done, CopyDone);
    }

    // A row whose second field group syncs independently of its content. Whichever group
    // is still waiting to be pushed keeps its local values; the other takes the server's.
    private void ApplyTwoGroupRows<T>(Table<T> table, List<T> rows, bool force, string secondGroup,
        Action<T, T> copySecondGroup) where T : SyncEntity
    {
        foreach (var row in rows)
        {
            var key = table.Key(row);
            table.Rows.TryGetValue(key, out var local);
            var contentPending = !force && IsMarked(table.Name, key, Content);
            var secondPending = !force && IsMarked(table.Name, key, secondGroup);

            if (row.IsDeleted || local is null || (!contentPending && !secondPending))
            {
                if (row.IsDeleted)
                {
                    Unmark(table.Name, key);
                }
                table.Put(row);
                continue;
            }
            if (contentPending)
            {
                if (!secondPending)
                {
                    copySecondGroup(local, row);
                }
                local.Sequence = row.Sequence;
                table.Dirty = true;
            }
            else
            {
                copySecondGroup(row, local);
                table.Put(row);
            }
        }
    }

    private void ApplyServerRows<T>(Table<T> table, List<T> rows, bool force) where T : SyncEntity
    {
        foreach (var row in rows)
        {
            var key = table.Key(row);
            if (row.IsDeleted)
            {
                Unmark(table.Name, key);
            }
            else if (!force && IsMarked(table.Name, key, Content))
            {
                continue;
            }
            table.Put(row);
        }
    }

    private static void CopyChecked(ListEntry to, ListEntry from)
    {
        to.IsChecked = from.IsChecked;
        to.CheckedAt = from.CheckedAt;
        to.CheckedBy = from.CheckedBy;
    }

    private static void CopyDone(TaskEntry to, TaskEntry from)
    {
        to.IsDone = from.IsDone;
        to.DoneAt = from.DoneAt;
        to.DoneBy = from.DoneBy;
    }

    private static void AddContent<T>(List<T> into, Table<T> table, string key) where T : SyncEntity
    {
        if (table.Rows.TryGetValue(key, out var row))
        {
            // A copy, so edits made while the push is in flight don't change what's sent.
            into.Add(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row))!);
        }
    }

    private void Mark<T>(Table<T> table, string key, string group) where T : SyncEntity
    {
        _meta.Pending[Marker(table.Name, key, group)] = ++_meta.Revision;
        _metaDirty = true;
    }

    private bool IsMarked(SyncTable table, string key, string group) =>
        _meta.Pending.ContainsKey(Marker(table, key, group));

    private void Unmark(SyncTable table, string key)
    {
        _metaDirty |= _meta.Pending.Remove(Marker(table, key, Content));
        _metaDirty |= _meta.Pending.Remove(Marker(table, key, Checked));
        _metaDirty |= _meta.Pending.Remove(Marker(table, key, Done));
    }

    private static string Marker(SyncTable table, string key, string group) => $"{table}|{key}|{group}";

    private static (SyncTable Table, string Key, string Group) ParseMarker(string marker)
    {
        var parts = marker.Split('|');
        return (Enum.Parse<SyncTable>(parts[0]), parts[1], parts[2]);
    }

    private async Task AfterLocalChangeAsync()
    {
        await PersistAsync();
        Changed?.Invoke();
        LocalChangeQueued?.Invoke();
    }

    private async Task PersistAsync()
    {
        foreach (var table in AllTables.Where(t => t.Dirty))
        {
            await table.SaveAsync(storage, Prefix);
        }
        if (_metaDirty)
        {
            await storage.SetAsync(Prefix + "meta", _meta);
            _metaDirty = false;
        }
    }

    private Table<T> TableFor<T>() where T : SyncEntity =>
        AllTables.OfType<Table<T>>().SingleOrDefault()
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not a synced table.");

    private ITable[] AllTables => [_categories, _stores, _orders, _items, _lists, _entries, _taskLists, _tasks];

    private interface ITable
    {
        SyncTable Name { get; }
        bool Dirty { get; }
        Task LoadAsync(ISyncStorage storage, string prefix);
        Task SaveAsync(ISyncStorage storage, string prefix);
        long? TryGetSequence(string key);
        void Remove(string key);
    }

    private sealed class Table<T>(SyncTable name, string storageKey, Func<T, string> key) : ITable where T : SyncEntity
    {
        public SyncTable Name => name;
        public Dictionary<string, T> Rows { get; private set; } = new();
        public bool Dirty { get; set; }
        public string Key(T row) => key(row);

        public void Put(T row)
        {
            Rows[key(row)] = row;
            Dirty = true;
        }

        public void Remove(string rowKey) => Dirty |= Rows.Remove(rowKey);

        public long? TryGetSequence(string rowKey) => Rows.TryGetValue(rowKey, out var row) ? row.Sequence : null;

        public async Task LoadAsync(ISyncStorage storage, string prefix) =>
            Rows = (await storage.GetAsync<List<T>>(prefix + storageKey) ?? []).ToDictionary(key);

        public async Task SaveAsync(ISyncStorage storage, string prefix)
        {
            await storage.SetAsync(prefix + storageKey, Rows.Values.ToList());
            Dirty = false;
        }
    }

    private sealed class Meta
    {
        public long Cursor { get; set; }
        public DateTimeOffset? LastSyncedAt { get; set; }
        public long Revision { get; set; }
        public Dictionary<string, long> Pending { get; set; } = new();
        public List<FamilyMember> Users { get; set; } = [];
    }
}

public sealed record PushSnapshot(SyncPushRequest Request, IReadOnlyDictionary<string, long> Revisions);
