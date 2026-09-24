using System.Security.Claims;
using Allo.Api.Auth;
using Allo.Api.Data;
using Allo.Shared.Auth;
using Allo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Sync;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/sync", Pull);
        api.MapPost("/sync", Push);
    }

    private static async Task<SyncPullResponse> Pull(AppDbContext db, long since = 0)
    {
        // One transaction, so the cursor and the rows come from the same snapshot: every
        // row up to the cursor is included, nothing past it is.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var cursor = await db.SyncCounter.Where(c => c.Id == 1).Select(c => c.Value).SingleAsync();
        var response = new SyncPullResponse
        {
            Cursor = cursor,
            Rows = new SyncRows
            {
                Categories = await db.Categories.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
                Stores = await db.Stores.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
                StoreCategoryOrders = await db.StoreCategoryOrders.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
                Items = await db.Items.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
                Lists = await db.ShoppingLists.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
                Entries = await db.ListEntries.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
                TaskLists = await db.TaskLists.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
                Tasks = await db.TaskEntries.AsNoTracking().Where(r => r.Sequence > since).ToListAsync(),
            },
            Users = await db.UserLogins
                .Join(db.Users, l => l.UserId, u => u.Id, (l, u) => new FamilyMember(u.Id, l.Username, u.DisplayName))
                .ToListAsync(),
        };
        await transaction.CommitAsync();
        return response;
    }

    private static Task<SyncPushResponse> Push(SyncPushRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        new SyncApplier(db, principal.UserId()).ApplyAsync(request);
}
