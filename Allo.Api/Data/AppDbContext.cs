using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreCategoryOrder> StoreCategoryOrders => Set<StoreCategoryOrder>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<SyncCounter> SyncCounter => Set<SyncCounter>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Unit>().HaveConversion<UnitConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(100);
        });

        modelBuilder.Entity<UserLogin>(e =>
        {
            e.HasKey(l => l.UserId);
            e.HasOne<User>().WithOne().HasForeignKey<UserLogin>(l => l.UserId);
            e.Property(l => l.Username).HasMaxLength(50);
            e.HasIndex(l => l.Username).IsUnique();
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId);
        });

        modelBuilder.Entity<Store>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Color).HasMaxLength(7);
        });

        modelBuilder.Entity<StoreCategoryOrder>(e =>
        {
            e.HasKey(o => new { o.StoreId, o.CategoryId });
            e.HasOne<Store>().WithMany().HasForeignKey(o => o.StoreId);
            e.HasOne<Category>().WithMany().HasForeignKey(o => o.CategoryId);
        });

        modelBuilder.Entity<Item>(e =>
        {
            e.Property(i => i.Name).HasMaxLength(200);
            e.Property(i => i.NormalizedName).HasMaxLength(200);
            // Not unique: two devices can create the same item offline. Duplicates are
            // merged by sync, not rejected by the database.
            e.HasIndex(i => i.NormalizedName);
            e.HasOne<Category>().WithMany().HasForeignKey(i => i.DefaultCategoryId);
        });

        modelBuilder.Entity<ShoppingList>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<ListEntry>(e =>
        {
            e.Property(le => le.Note).HasMaxLength(500);
            e.HasIndex(le => le.ListId);
            e.HasOne<ShoppingList>().WithMany().HasForeignKey(le => le.ListId);
            e.HasOne<Item>().WithMany().HasForeignKey(le => le.ItemId);
            e.HasOne<Category>().WithMany().HasForeignKey(le => le.CategoryId);
            e.HasOne<Store>().WithMany().HasForeignKey(le => le.StoreId);
            e.HasOne<User>().WithMany().HasForeignKey(le => le.AddedBy);
            e.HasOne<User>().WithMany().HasForeignKey(le => le.CheckedBy);
        });

        modelBuilder.Entity<SyncCounter>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever();
        });

        // Shared sync columns: every pull filters on Sequence, and UpdatedBy is a real user.
        foreach (var type in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(SyncEntity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(type.ClrType, e =>
            {
                e.HasIndex(nameof(SyncEntity.Sequence));
                e.HasOne(typeof(User)).WithMany().HasForeignKey(nameof(SyncEntity.UpdatedBy));
            });
        }

        // Rows are never hard deleted (tombstones), so no cascades anywhere.
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }

        SeedData.Apply(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries<SyncEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (changed.Any(e => e.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Synced rows are never hard deleted. Set IsDeleted instead.");
        }

        if (changed.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        // Reserving the numbers takes SQLite's write lock, which is held until commit, so
        // sequence numbers become visible in commit order. A client that has seen N can
        // never later miss a row with a number below N.
        var ownTransaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var last = (await Database
                .SqlQuery<long>($"UPDATE SyncCounter SET Value = Value + {changed.Count} WHERE Id = 1 RETURNING Value")
                .ToListAsync(cancellationToken)).Single();

            var next = last - changed.Count;
            foreach (var entry in changed)
            {
                entry.Entity.Sequence = ++next;
            }

            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken);
            }
            return result;
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }
        }
    }
}
