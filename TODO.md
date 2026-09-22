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
- `Item` (id, name, defaultCategoryId, defaultUnit, aliases, defaultTags, notes, lastUsedAt)
  — the reusable catalog, separate from list entries. `defaultUnit` pre-fills the entry's
  unit ("ground beef" → `lb`, "bananas" → `bunch`), overridable per entry
- `ListEntry` (id, listId, itemId, categoryId, quantity, unit, note, storeId, tags,
  addedBy, updatedAt, updatedBy, isChecked, checkedAt, checkedBy, isDeleted, sequence)
  - `quantity` is `decimal`, `unit` is the `Unit` enum (see Conventions)
  - `sequence` is server-assigned on every accepted change, never set by the client
  - content group (quantity, unit, note, categoryId, storeId, tags) is covered by
    `updatedAt`/`updatedBy`; checked group (`isChecked`) by `checkedAt`/`checkedBy`

**Categories are global; stores only define ordering over them.** Do not hang categories
off stores directly, or every item needs a category assignment per store.

**Tags are metadata, not sorting drivers.** Category stays required on every entry and is
the only thing that drives sort order. Tags are optional and free-form.

**Store tags as a JSON array column on `ListEntry`**, not a join table. A join table
breaks last-write-wins because membership rows sync individually and adds race against
deletes. Inline keeps the entry a single atomic sync unit. EF Core 8+ maps primitive
collections natively.

### Sync model

Last-write-wins per field group, with a server-assigned sequence number as the only
sync cursor. Client clocks are never used to decide anything.

- **Sequence cursor:** the server keeps a monotonic counter (SQLite has no `rowversion`,
  so an AUTOINCREMENT change-log table or a single counter row). Every accepted change
  stamps the entry's `Sequence` with the next number. The client stores the highest
  sequence it has seen and pulls "everything with `Sequence` > N", tombstones included.
- **Two field groups per entry, resolved independently:**
  - *Content:* quantity, unit, note, category, store, tags. Metadata: `UpdatedAt`, `UpdatedBy`.
  - *Checked state:* `IsChecked`. Metadata: `CheckedAt`, `CheckedBy`.

  So one person changing milk from 1 to 2 while another checks it off, both offline,
  keeps both changes. Plain row-level LWW would lose one.
- **Resolution rule: last to reach the server wins, per field group.** "Last" means
  server receive order, never device time. The server writes the incoming field group,
  stamps a new `Sequence`, and returns the result. No base version is sent and there is
  no merge logic or conflict prompt (nobody should be resolving conflicts mid-aisle).
  Two offline edits to the same group: the later sync wins, which is usually the fresher
  intent.
- **Tombstones:** `IsDeleted` soft delete, never hard delete. **Deletes are final:** an
  edit arriving for an entry that is already deleted is ignored, so a device coming back
  online cannot resurrect items someone else cleared.
- **Timestamps are informational:** `UpdatedAt` and `CheckedAt` are for display ("checked
  by Sam at 3:14"), not conflict resolution.

Real conflicts are rare on a shopping list: one person checking off milk and another
adding bread do not collide.

No SignalR in v1. Refresh on app focus plus pull-to-refresh is enough.

### Conventions

- Client-generated GUIDs for all ids, so offline creates work without a round trip
- Normalize item and tag names to lowercase for matching, display original casing
- "Uncategorized" is a real category that always sorts last, never a null
- **Quantity** is a `decimal`, required, defaults to 1, must be > 0 (server rejects otherwise)
- **Unit** is a C# enum in `Allo.Shared`, never null, defaults to `ea`:
  - Count units (whole numbers only, +/- stepper in the UI): `ea`, `bunch`, `dozen`.
    `ea` covers cans, packages, bags, etc.
  - Measure units (decimals allowed, decimal keypad in the UI): `g`, `kg`, `lb`, `ml`, `l`, `gal`
  - Stored as the string code in the DB and in JSON, never the integer, so the data stays
    readable and adding a member never renumbers existing ones. Add new members at the end
  - `ea` is hidden on display ("2 milk"); other units show ("1.5 kg ground beef")
  - No conversion between units; 500 g and 1 lb stay as entered
  - Tradeoff accepted: an older client can't parse a newly added unit. The service worker
    update path (PWA section) is what keeps clients current

---

## Phase 0: Foundations

- [x] Scaffold solution: `Allo.Client` (Blazor WASM PWA), `Allo.Api` (minimal API), `Allo.Shared` (DTOs/models)
- [x] EF Core + SQLite, initial migration, auto-migrate on startup
- [x] Health check endpoint (`/healthz`, includes a DB check)
- [x] Local dev run: API serving the WASM output same-origin, so dev matches prod
- [x] Decide and document unit handling: `Unit` enum stored as string codes, `decimal` quantity (see Conventions)

## Data Model

- [ ] `Category` with self-referencing `ParentId`, seeded top-level set (Produce, Bakery, Dairy, Meat & Seafood, Frozen, Pantry, Beverages, Snacks, Household, Personal Care, Baby, Pet, Uncategorized)
- [ ] `Store` + `StoreCategoryOrder`, with a sensible default order applied to any new store
- [ ] `Unit` enum in `Allo.Shared` (`ea`, `g`, `kg`, `lb`, `ml`, `l`, `gal`, `bunch`, `dozen`), with an `IsCountUnit` helper; EF `HasConversion<string>()` and `JsonStringEnumConverter` so it's never stored or sent as an int
- [ ] `Item` catalog table with `DefaultUnit`, and `Aliases` and `DefaultTags` as JSON columns
- [ ] `ShoppingList` + `ListEntry`, with `CategoryId`, `AddedBy`, `UpdatedAt`, `UpdatedBy`, `IsChecked`, `CheckedAt`, `CheckedBy`, `IsDeleted`, `Sequence`
- [ ] Sequence counter (change-log table or single counter row) that stamps `ListEntry.Sequence` on every accepted change
- [ ] Index on `ListEntry.Sequence` (every sync pull filters on it)

## Catalog & Categorization

- [ ] Seed 400 to 600 common Canadian grocery items as JSON: name, category, default unit, aliases. Generic level only ("milk", "cheddar", "ground beef"), not brand level
- [ ] Three-pass match on item entry:
  1. exact match on normalized name or alias
  2. token containment ("sourdough bread" contains "bread" so suggest Bakery)
  3. no match, default to Uncategorized with the category picker pre-focused
- [ ] Pass 2 produces a **suggestion, not a silent assignment.** Pre-fill it, let the user change it in one tap. This is the specific Flipp failure being fixed
- [ ] Learn from corrections: when a user categorizes a new item, write it to the catalog so the next occurrence is automatic. Same for units: if an item's unit keeps being changed away from its default, update `DefaultUnit`
- [ ] Autocomplete on add, ranked by `LastUsedAt` and frequency, so staples surface first
- [ ] Category manager UI: add, rename, nest, reorder, merge two categories
- [ ] Do not attempt to seed hardware or home goods. Groceries repeat weekly, one-off items do not, and categorizing those by hand once is fine

## Lists & Entries

- [ ] Add entry: type-ahead against catalog, quantity, unit (pre-filled from the item's `DefaultUnit`), optional note
- [ ] Quantity input switches by unit: +/- stepper for count units, decimal keypad for measure units
- [ ] Validation, client and server: quantity > 0, whole numbers only for count units
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
