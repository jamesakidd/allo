# Allo: Feature / Bug Backlog

Working list of planned work for **Allo**, a self-hosted family shopping list app.
Tackle one at a time; check items off as completed. Add discovered work to the
relevant section rather than doing it silently.

---

## Project Context

**What it is:** a shopping list PWA for family use, replacing Flipp. Self-hosted on
Unraid. No flyers, no deals, no scraping. The entire value is fast entry, user-owned
categorization, and sync that actually works on bad signal.
Sync cursors are server sequence numbers, never timestamps. Client clocks are not trusted for conflict resolution.

**The constraint that drives everything:** grocery stores have dead spots. The app must
be fully usable with the network off, and reconcile when it comes back. Every user
action writes locally and updates the UI immediately. The network is never in the path
of the user seeing their own change.

### Stack

- **Frontend:** standalone Blazor WebAssembly, PWA enabled (`dotnet new blazorwasm --pwa`).
  Not Blazor Server (a dropped SignalR connection in a store means a frozen UI).
  Not the .NET 8+ "Blazor Web App" auto render mode template.
- **Backend:** ASP.NET Core minimal API.
- **Database:** SQLite via EF Core. Single file in appdata, trivial backup.
- **Local storage:** `Blazored.LocalStorage`. The dataset is small enough that IndexedDB
  is not worth the interop.
- **Packaging:** one Docker image, API serves the published WASM files same-origin.
  Same pattern as EWD ERP, which also solves the auth cookie question.

### Data model

- `ShoppingList` (id, name) — defaults to one list, supports a second for the
  "watch list" style use Flipp had
- `Store` (id, name)
- `Category` (id, name, parentId nullable) — adjacency list, global, not per-store
- `StoreCategoryOrder` (storeId, categoryId, sortOrder) — each store defines its own
  walking order over the shared categories
- `Item` (id, name, defaultCategoryId, aliases, defaultTags, notes, lastUsedAt) — the
  reusable catalog, separate from list entries
- `ListEntry` (id, listId, itemId, quantity, unit, note, isChecked, storeId, tags,
  addedBy, updatedAt, isDeleted)

**Categories are global; stores only define ordering over them.** Do not hang categories
off stores directly, or every item needs a category assignment per store.

**Tags are metadata, not sorting drivers.** Category stays required on every entry and is
the only thing that drives sort order. Tags are optional and free-form.

**Store tags as a JSON array column on `ListEntry`**, not a join table. A join table
breaks last-write-wins because membership rows sync individually and adds race against
deletes. Inline keeps the entry a single atomic sync unit. EF Core 8+ maps primitive
collections natively.

### Sync model

Item-level last-write-wins. Every entry carries `updatedAt` and an `isDeleted` tombstone
(never hard delete). Client tracks `lastSyncedAt`, pushes pending changes, pulls anything
newer. Real conflicts are near-nonexistent on a shopping list: one person checking off
milk and another adding bread do not collide.

No SignalR in v1. Refresh on app focus plus pull-to-refresh is enough.

### Conventions

- Client-generated GUIDs for all ids, so offline creates work without a round trip
- Normalize item and tag names to lowercase for matching, display original casing
- "Uncategorized" is a real category that always sorts last, never a null

---

## Phase 0: Foundations

- [ ] Scaffold solution: `Allo.Client` (Blazor WASM PWA), `Allo.Api` (minimal API), `Allo.Shared` (DTOs/models)
- [ ] EF Core + SQLite, initial migration, auto-migrate on startup
- [ ] Health check endpoint
- [ ] Local dev run: API serving the WASM output same-origin, so dev matches prod
- [ ] Decide and document unit handling (free text vs enum for ea/kg/lb/pkg/bunch)

## Data Model

- [ ] `Category` with self-referencing `ParentId`, seeded top-level set (Produce, Bakery, Dairy, Meat & Seafood, Frozen, Pantry, Beverages, Snacks, Household, Personal Care, Baby, Pet, Uncategorized)
- [ ] `Store` + `StoreCategoryOrder`, with a sensible default order applied to any new store
- [ ] `Item` catalog table with `Aliases` and `DefaultTags` as JSON columns
- [ ] `ShoppingList` + `ListEntry`, with `UpdatedAt`, `IsDeleted`, `AddedBy`
- [ ] Index on `ListEntry.Sequence` (every sync pull filters on it)

## Catalog & Categorization

- [ ] Seed 400 to 600 common Canadian grocery items as JSON: name, category, aliases. Generic level only ("milk", "cheddar", "ground beef"), not brand level
- [ ] Three-pass match on item entry:
  1. exact match on normalized name or alias
  2. token containment ("sourdough bread" contains "bread" so suggest Bakery)
  3. no match, default to Uncategorized with the category picker pre-focused
- [ ] Pass 2 produces a **suggestion, not a silent assignment.** Pre-fill it, let the user change it in one tap. This is the specific Flipp failure being fixed
- [ ] Learn from corrections: when a user categorizes a new item, write it to the catalog so the next occurrence is automatic
- [ ] Autocomplete on add, ranked by `LastUsedAt` and frequency, so staples surface first
- [ ] Category manager UI: add, rename, nest, reorder, merge two categories
- [ ] Do not attempt to seed hardware or home goods. Groceries repeat weekly, one-off items do not, and categorizing those by hand once is fine

## Lists & Entries

- [ ] Add entry: type-ahead against catalog, quantity, unit, optional note
- [ ] Check off / uncheck, with checked items collapsing to the bottom of their category
- [ ] Group by category, sorted by the active store's `StoreCategoryOrder`
- [ ] Store selector at the top, re-sorting the same list into that store's walking order
- [ ] Store view shows that store's entries plus anything with no store assigned
- [ ] Clear checked items (soft delete, tombstoned)
- [ ] Edit entry (quantity, unit, note, category, store, tags)
- [ ] Second list support for watch-list style items

## Tags

- [ ] Free-form tags on `ListEntry`, autocomplete off existing tags
- [ ] `DefaultTags` on catalog items, applied on add, overridable per entry
- [ ] Seed suggested tags: `if on sale`, `urgent`, `next trip`, `bulk`, `check price`, `optional`, `exact brand`
- [ ] Visual treatment on the list row (small chips, not full-width, must not crowd the item name)
- [ ] Filter the list by tag
- [ ] Guardrail: tags must never affect sort order

## Offline & Sync

- [ ] Local store in `Blazored.LocalStorage` holding the full list state
- [ ] Optimistic writes: UI updates immediately, change queued for sync
- [ ] Pending change queue, survives app close and reload
- [ ] `GET /api/sync?since={seq}` returning changed entries including tombstones
- [ ] `POST /api/sync` accepting a batch of client changes, LWW resolution server-side
- [ ] Sync on app focus, on reconnect, and on pull-to-refresh
- [ ] Sync status indicator: last synced time and pending change count. Not a spinner. "3 changes pending" reads as working, an ambiguous spinner reads as broken
- [ ] Test properly in airplane mode: add, check off, edit, delete, then reconnect
- [ ] Split checked state into its own resolution field set: `IsChecked`, `CheckedAt`, `CheckedBy`, resolved independently of the `UpdatedAt` that covers quantity, unit, note, category, and tags. Two resolution rules per entry, not one. Handles the real collision: she changes milk 1 to 2 while you check milk off, both offline. Plain row-level LWW loses one of those changes. Do this before building sync, not after (retrofitting means a migration and a protocol change)
- [ ] Server-side monotonic sequence counter instead of timestamps for the sync cursor. Every accepted change gets the next number; client cursor is that number, pull is "everything with seq > N". Immune to phone clock drift, which otherwise lets a device that is 10 minutes fast win every conflict for 10 minutes. SQLite has no `rowversion`, so use an AUTOINCREMENT change-log table or a single counter row

## PWA & Mobile UX

- [ ] Web manifest, icons, splash, standalone display mode
- [ ] Service worker caching the app shell, with a working update path (no stale-forever builds)
- [ ] Trim and Brotli-compress the WASM payload, measure cold start on cell data
- [ ] Touch targets sized for one-handed use in a store, checkbox hit area generous
- [ ] Dark theme (the example app is dark and it is the right call for a store)
- [ ] Keep the keyboard up when adding multiple items in a row
- [ ] Verify home screen install on Android

## Auth

- [ ] Cookie auth, same pattern as EWD ERP: cookie middleware + standalone `PasswordHasher<TUser>`, simple `Users` table. Not full ASP.NET Core Identity, not an external provider
- [ ] Long expiry with sliding renewal (nobody should be logged out mid-shop)
- [ ] `[Authorize]` on all API endpoints as the real boundary; `AuthorizeView` gates the UI
- [ ] Family-scale only: 4 or 5 accounts, no roles, no public sign-up. Accounts created manually or by a simple invite token
- [ ] Confirm the service worker and offline flow behave when the cookie has expired (do not silently discard queued changes)

## Deployment

- [ ] Multi-stage Dockerfile, one image, API serves the WASM output
- [ ] GitHub Actions on version tags, push to GHCR (`ghcr.io/jamesakidd/allo:vX.Y.Z` + `latest`)
- [ ] Unraid container template, all config via env vars (double-underscore keys)
- [ ] SQLite file on a bind-mounted appdata volume
- [ ] NPM reverse proxy on a No-IP subdomain with a valid Let's Encrypt cert (required for service worker and home screen install; do not rely on Tailscale, family members will not have the tailnet up in a store)
- [ ] Backup: scheduled copy of the SQLite file, plus a manual copy before any container update
- [ ] Security review for internet exposure, same considerations as EWD ERP

## Misc / Cosmetic

- [ ] App icon and favicon
- [ ] Docker logo for the Unraid dockers page
- [ ] Empty state for a fresh list
- [ ] Import: paste a block of text, one item per line, bulk-add with categorization suggestions

## Parked / Later

- [ ] Real-time push via SignalR (only if focus-refresh proves insufficient)
- [ ] Price history per item per store, manually entered
- [ ] Recurring staples with a suggested cadence
- [ ] Barcode scan to add (browser camera API, accuracy will be the problem)
- [ ] Meal planning that generates list entries
