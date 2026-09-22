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
- **UI:** MudBlazor, dark theme, system fonts only (nothing downloaded, works offline).
  Snackbars at the bottom so they don't cover the app bar.
- **Local storage:** `Blazored.LocalStorage`. The dataset is small enough that IndexedDB
  is not worth the interop.
- **Packaging:** one Docker image, API serves the published WASM files same-origin.
  Same pattern as EWD ERP, which also solves the auth cookie question.

### Data model

Entity classes are plain POCOs in `Allo.Shared/Models`, shared by the API and the client's
offline store. All EF configuration lives in `Allo.Api` (fluent API, no attributes), so
the WASM client never references EF.

**Every table except `User` syncs** and inherits `SyncEntity`: `sequence`, `isDeleted`,
`updatedAt`, `updatedBy`. One global counter feeds all tables, so the client keeps a
single cursor. Rows are never hard deleted (`SaveChanges` throws), and every FK is
`Restrict`.

- `User` (id, displayName) — minimal until the Auth phase adds credentials. Not synced
- `ShoppingList` (id, name) — defaults to one list, supports a second for the
  "watch list" style use Flipp had. Seeded "Groceries" with a fixed id
- `Store` (id, name) — none seeded
- `Category` (id, name, parentId nullable, sortOrder) — adjacency list, global, not
  per-store. `sortOrder` is the default walking order, relative to siblings; it applies
  when no store is selected and is copied into `StoreCategoryOrder` for a new store.
  Seeded with fixed ids; Uncategorized has a well-known id (`Category.UncategorizedId`)
- `StoreCategoryOrder` (storeId, categoryId, sortOrder) — each store defines its own
  walking order over the shared categories. Keyed on (storeId, categoryId), not its own
  id, so two devices ordering the same store offline update one row, not create two
- `Item` (id, name, normalizedName, defaultCategoryId, defaultUnit, pendingUnit, aliases,
  defaultTags, notes, lastUsedAt, useCount) — the reusable catalog, separate from list
  entries. `defaultUnit` pre-fills the entry's unit ("ground beef" → `lb`, "bananas" →
  `kg`), overridable per entry. `useCount` + `lastUsedAt` rank autocomplete; `pendingUnit`
  holds a non-default unit until it's chosen twice in a row. Seeded items have name-based
  (UUID v5) ids, so every install agrees on them
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

- **Duplicate new items merge:** a new item whose name matches an existing one isn't
  inserted; its entries point at the existing item and the phone is told to swap ids.
- **One bad row never blocks the queue:** rejected rows come back with the server's
  version, and the phone drops its change.

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

- [x] `SyncEntity` base, minimal `User` table, entities in `Allo.Shared`, EF config in `Allo.Api`
- [x] `Category` with self-referencing `ParentId`, seeded top-level set (Produce, Bakery, Dairy, Meat & Seafood, Frozen, Pantry, Beverages, Snacks, Household, Personal Care, Baby, Pet, Uncategorized)
- [x] `Store` + `StoreCategoryOrder` tables, `Category.SortOrder` as the default walking order
- [ ] Copy `Category.SortOrder` into `StoreCategoryOrder` when a store is created (lands with the store create endpoint)
- [x] `Unit` enum in `Allo.Shared` (`ea`, `g`, `kg`, `lb`, `ml`, `l`, `gal`, `bunch`, `dozen`), members named in full (`Unit.Kilogram`) with `IsCountUnit`/`ToCode`/`FromCode` helpers; EF `UnitConverter` and `[JsonStringEnumMemberName]` store and send the lowercase code, never an int
- [x] `Item` catalog table with `DefaultUnit`, and `Aliases` and `DefaultTags` as JSON columns
- [x] `ShoppingList` + `ListEntry`, with `CategoryId`, `AddedBy`, `UpdatedAt`, `UpdatedBy`, `IsChecked`, `CheckedAt`, `CheckedBy`, `IsDeleted`, `Sequence`
- [x] Sequence counter: single-row `SyncCounter` table, reserved in `SaveChanges` inside the write transaction, stamps `Sequence` on every synced table
- [x] Index on `Sequence` for every synced table (every sync pull filters on it)

## Catalog & Categorization

- [x] Seed 400 to 600 common Canadian grocery items as JSON: name, category, default unit, aliases. Generic level only ("milk", "cheddar", "ground beef"), not brand level
  - 599 items in `Allo.Api/Data/Seed/catalog.json`, top-level categories only. Units: ground/bulk meat `lb`, deli meat and deli cheese `g`, eggs `dozen`, bananas `kg`, herbs and greens `bunch`, everything else `ea`
  - Aliases are synonyms only ("pop" → soft drinks, "kd" → macaroni and cheese), never variants like "2% milk": an alias match shows the item's name, so variants would hide what was typed. Plurals are handled by the matcher, not aliases
  - Seeded at startup, insert-missing only: an item that exists by id or name (deleted or not) is never touched, so edits and deletes stick while new seed items still reach existing installs
- [x] Three-pass match on item entry (`CatalogMatcher` in `Allo.Shared`, runs offline on the client):
  1. exact match on normalized name or alias
  2. token containment ("sourdough bread" contains "bread" so suggest Bakery)
  3. no match, default to Uncategorized with the category picker pre-focused
  - Singular/plural forms match both ways ("grape" ↔ "grapes", "berry" ↔ "berries")
  - Pass 2 prefers a phrase ending on the last word (the head noun): "bread flour" → Pantry, "peanut butter cookies" → Snacks
- [x] Pass 2 produces a **suggestion, not a silent assignment.** (Matcher returns `MatchKind.Suggested`, distinct from `Exact`; the one-tap UI lands with Add entry) Pre-fill it, let the user change it in one tap. This is the specific Flipp failure being fixed
- [x] Learn from corrections: when a user categorizes a new item, write it to the catalog so the next occurrence is automatic. Same for units: if an item's unit keeps being changed away from its default, update `DefaultUnit` (`CatalogLearning.RecordAdd`: a category change applies immediately; a unit needs the same non-default choice twice in a row)
- [x] Autocomplete on add, ranked by `LastUsedAt` and frequency, so staples surface first (`CatalogAutocomplete`: `UseCount` plus a recency boost that halves every 14 days, then name prefix > alias prefix > later word)
- [x] Category manager UI (`/categories`): add, rename, nest ("move under"), reorder (up/down among siblings), merge two categories. Deleting one moves its contents to Uncategorized, never leaving a dangling reference
- [x] Do not attempt to seed hardware or home goods. Groceries repeat weekly, one-off items do not, and categorizing those by hand once is fine

## Lists & Entries

Screen logic is in `Allo.Shared/Lists` (`ListView` groups and orders, `ListActions` performs
every change) so it's testable without a browser; the Razor pages are a thin shell over it.

- [x] Add entry: one line pinned above the keyboard. Type-ahead against the catalog (`CatalogAutocomplete`), enter adds with quantity 1 and the item's `DefaultUnit`, and focus stays in the box for the next item
- [x] Add entry categorization UI on `CatalogMatcher`: exact match fills silently; a suggestion shows as a chip under the box ("check the category") that opens the picker in one tap; no match lands in Uncategorized the same way. `CatalogLearning.RecordAdd` runs on every add
- [x] Adding something already on the list adds to that row instead of making a second one (same item, same unit, not checked)
- [x] Quantity input switches by unit: +/- stepper for count units, decimal field for measure units
- [x] Validation, client and server: quantity > 0, whole numbers only for count units (shared `SyncValidation`, checked on a copy so a rejected edit never lands on screen)
- [x] Check off / uncheck: checked items move to an "In the cart" section below the live list, still grouped by category so they can be reviewed; unchecking puts them straight back
- [x] Group by category, sorted by the active store's `StoreCategoryOrder`, subcategories staying with their parent
- [x] Store selector at the top, re-sorting the same list into that store's walking order (verified: after moving Bakery above Produce globally, a store keeps its own order)
- [x] Store view shows that store's entries plus anything with no store assigned (the store dropdown is a filter first: picking one hides other stores' items, "Any store" shows everything)
- [x] Store pill on the entry row, shown wherever the entry has a store; entries with no store show none. Adding while a store is selected attaches that store
- [x] Store colour (`Store.Color`, hex): set with a colour picker when adding or editing a store, shown on the row pill and as a dot in the store filter. Validated to a plain `#rrggbb` on client and server, since it goes into a style attribute
- [x] Clear checked items (soft delete, tombstoned)
- [x] Edit entry (quantity, unit, note, category, store) — tags land with the Tags section
- [x] Second list support for watch-list style items (`/lists`: add, rename, delete; a list selector appears once there's more than one)
- [x] Stores screen (`/stores`): add (copies the default category order), rename, delete. Deleting a store leaves its entries on the list with no store
- [ ] Reorder categories per store (drag or up/down on the store's own order). Until then every store starts from, and keeps, the default order
- [x] Active list and store are remembered per device, not synced: two people in different stores don't move each other's view

## Tags

- [x] Free-form tags on `ListEntry`, autocomplete off existing tags (tag editor in the edit sheet: chips with autocomplete, plus one-tap suggestions)
- [x] Type tags while adding: `ribeye #sale`. Single word per `#tag`; multi-word tags go in via the editor
- [x] `DefaultTags` on catalog items, applied on add, overridable per entry. Learned like units: the same tags typed twice in a row become the item's defaults (`Item.PendingTags`). Adding with no tags means "use the defaults", so it never clears them
- [x] Seed suggested tags: `if on sale`, `urgent`, `next trip`, `bulk`, `check price`, `optional`, `exact brand` (offered until real ones exist; used tags rank first)
- [x] Visual treatment on the list row (small chips, not full-width, must not crowd the item name): under the name, muted, below the store pill
- [x] Filter the list by tag: a chip row under the selectors, only when something on the list is tagged. Filters both the live list and the cart
- [x] Guardrail: tags must never affect sort order (covered by a test)

## Offline & Sync

Engine lives in `Allo.Shared/Sync` (`LocalStore`, `SyncEngine`, `HttpSyncTransport`) behind an
`ISyncStorage` interface, so tests run real "phones" against the real server. The Blazor
client plugs in browser storage (`BrowserSyncStorage`) and decides when to sync (`SyncCoordinator`).

- [x] Local store in `Blazored.LocalStorage` holding the full list state: every synced table, one key per table (`allo.sync.*`), loaded before first render so the app opens with data offline
- [x] Optimistic writes: UI updates immediately, change queued for sync (`LocalStore.SaveAsync` / `DeleteAsync` / `SetCheckedAsync`)
- [x] Pending change queue, survives app close and reload. Holds markers per row and field group, not copies, so repeated edits to one row push once. Revision numbers keep an edit made during a push from being cleared by that push's response
- [x] `GET /api/sync?since={seq}` returning changed rows of every synced table including tombstones, plus the family list, read in one transaction so the cursor matches the rows
- [x] `POST /api/sync` accepting a batch of client changes, LWW resolution server-side. Who comes from the login, never the payload. Each row saved on its own: an invalid row is rejected (with the server's current version returned) instead of failing the batch and stalling the queue forever
- [x] Sync on app focus, on reconnect, on start/login, and about a second after local changes stop. Tapping the status indicator syncs now
- [x] Pull-to-refresh gesture on the list screen (drag down at the top; the page follows your finger and syncs past ~70px)
- [x] Sync status indicator: last synced time and pending change count. Not a spinner. "3 changes pending" reads as working, an ambiguous spinner reads as broken (`SyncStatus` in the app bar: "Synced 2 min ago", "3 changes pending", "Offline")
- [x] Duplicate catalog items: two devices can create "oat milk" offline with different ids (`Item.NormalizedName` is deliberately not unique). Sync must merge them: keep one, repoint entries, tombstone the other. (Built as: the server never inserts the second one; it repoints that push's entries and returns an `ItemRemap`, and the phone swaps the id locally)
- [x] Test properly in airplane mode: add, check off, edit, delete, then reconnect (automated in `SyncEngineTests` with two simulated phones)
- [ ] Same airplane-mode run on real phones once deployed
- [x] Split checked state into its own resolution field set: `IsChecked`, `CheckedAt`, `CheckedBy`, resolved independently of the `UpdatedAt` that covers quantity, unit, note, category, and tags. Two resolution rules per entry, not one. Handles the real collision: she changes milk 1 to 2 while you check milk off, both offline. Plain row-level LWW loses one of those changes. Do this before building sync, not after (retrofitting means a migration and a protocol change)
- [x] Server-side monotonic sequence counter instead of timestamps for the sync cursor. Every accepted change gets the next number; client cursor is that number, pull is "everything with seq > N". Immune to phone clock drift, which otherwise lets a device that is 10 minutes fast win every conflict for 10 minutes. SQLite has no `rowversion`, so use an AUTOINCREMENT change-log table or a single counter row

## PWA & Mobile UX

- [ ] Web manifest, icons, splash, standalone display mode
- [ ] Service worker caching the app shell, with a working update path (no stale-forever builds)
- [ ] Trim and Brotli-compress the WASM payload, measure cold start on cell data
- [x] Touch targets sized for one-handed use in a store, checkbox hit area generous (48px rows, large checkboxes, add bar at the bottom within thumb reach)
- [x] Keep the keyboard up when adding multiple items in a row
- [x] Dark theme (the example app is dark and it is the right call for a store)
- [ ] Verify home screen install on Android

## Auth

- [x] Cookie auth, same pattern as EWD ERP: cookie middleware + standalone `PasswordHasher<TUser>`, simple `Users` table. Not full ASP.NET Core Identity, not an external provider
  - Credentials live in a server-only `UserLogins` table (username, hash, `MustChangePassword`), apart from the shared `User` model, so a hash can never be serialized to a client
  - The cookie is re-checked against `UserLogins` on every request, so a cookie for a missing account gets a clean 401
  - Cookie encryption keys persist to `appdata/keys` (`DataProtection__KeysPath`), or every container update would log everyone out
- [x] Long expiry with sliding renewal (nobody should be logged out mid-shop): 1 year sliding, persistent cookie
- [x] `[Authorize]` on all API endpoints as the real boundary; `AuthorizeView` gates the UI (`/api` group requires auth; login/logout opt out. Client: `[Authorize]` on every page via `_Imports.razor`, login opts out)
- [x] Family-scale only: 4 or 5 accounts, no roles, no public sign-up. Accounts created manually or by a simple invite token
  - First account from `Admin__Username` / `Admin__InitialPassword` / `Admin__DisplayName` when none exist; after that any member adds others on the Family page with a temporary password
  - Temporary and initial passwords must be changed at first login (the app routes to Account until they are). Minimum 8 characters
  - Dev bootstrap in `appsettings.Development.json`: `dev` / `allo-dev-pass`
- [x] Change own password, rename display name
- [x] Login rate limiting: 5 attempts per minute per client address, 429 after that
- [x] Client remembers the logged-in user on the device, so the app opens with no signal; only a real 401 logs out
- [ ] Remove a family member (not built; would need a tombstone rather than a delete, since entries reference users)
- [x] Confirm the offline flow behaves when the cookie has expired (do not silently discard queued changes): a 401 during sync sends the user to log in and keeps the queue, which goes up after login
- [ ] Re-check the expired-cookie flow once the service worker caches the app shell (PWA section)
- [x] Logging out with unsynced changes warns, and keeps them on the device for the next login

## Deployment

- [ ] Multi-stage Dockerfile, one image, API serves the WASM output
- [ ] GitHub Actions on version tags, push to GHCR (`ghcr.io/jamesakidd/allo:vX.Y.Z` + `latest`)
- [ ] Unraid container template, all config via env vars (double-underscore keys)
- [ ] SQLite file on a bind-mounted appdata volume
- [ ] NPM reverse proxy on a No-IP subdomain with a valid Let's Encrypt cert (required for service worker and home screen install; do not rely on Tailscale, family members will not have the tailnet up in a store)
- [ ] Backup: scheduled copy of the SQLite file, plus a manual copy before any container update
- [ ] Forwarded headers for NPM: trust `X-Forwarded-For`/`-Proto` from the proxy only (`KnownProxies`). Without it the login rate limiter sees one address (the proxy) for everyone, and the cookie's `SameAsRequest` secure flag sees plain http
- [ ] Security review for internet exposure, same considerations as EWD ERP

## Misc / Cosmetic

- [ ] App icon and favicon
- [ ] Docker logo for the Unraid dockers page
- [x] Empty state for a fresh list
- [ ] Import: paste a block of text, one item per line, bulk-add with categorization suggestions

## Parked / Later

- [ ] Real-time push via SignalR (only if focus-refresh proves insufficient)
- [ ] Price history per item per store, manually entered
- [ ] Recurring staples with a suggested cadence
- [ ] Barcode scan to add (browser camera API, accuracy will be the problem)
- [ ] Meal planning that generates list entries
