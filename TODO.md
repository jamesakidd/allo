# Allo: Feature / Bug Backlog

Working list of planned work for **Allo**, a self-hosted family shopping list app.
Tackle one at a time; check items off as completed. Add discovered work to the
relevant section rather than doing it silently.

---

## Project Context

**What it is:** a shopping list PWA for family use, replacing Flipp. Self-hosted on
Unraid. No flyers, no deals, no scraping. The entire value is fast entry, user-owned
categorization, and sync that actually works on bad signal.

**Scope grew once, deliberately (2026-09-24):** a household task list on a separate screen
(see Tasks). The offline sync engine is the expensive part of this app and it is generic, so
a second kind of list is mostly UI. The rule that keeps this from becoming bloat: tasks get
their own screen, their own tables and their own sort rules, and **nothing about them may
appear on the grocery list screen**.
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
- [x] Copy `Category.SortOrder` into `StoreCategoryOrder` when a store is created (`ListActions.CreateStore`)
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

## Tasks

A household task list, separate from the groceries. Asked for 2026-09-24. Priorities were
the headline ask; due dates came with it, and a way to push a due date into the phone's own
calendar is a stretch goal.

**Its own tables, not a `Kind` flag on `ShoppingList`.** A shopping list and a task list
share only an id and a name — different screens, different selectors, different sort rules.
A discriminator would mean every existing list query has to remember to filter, which is the
same class of bug as a tenancy leak, just smaller. Two plain tables need no filtering
anywhere. Same reasoning rules out reusing `ListEntry`: it requires an `ItemId` into the
catalog and a `CategoryId` that drives sort order, and a task has neither. Making those
nullable would weaken the invariants the matcher and the category sort depend on, and minting
a fake catalog item per task would pollute the catalog that learns categories and the
autocomplete that ranks by use count. "milk" belongs in that catalog; "call the plumber" does
not.

- [x] `TaskList` (id, name) and `TaskEntry` (id, taskListId, title, priority, dueOn, note, addedBy, isDone, doneAt, doneBy), both `SyncEntity`
  - Two field groups, mirroring `ListEntry` exactly so conflict resolution is the existing logic: content (title, priority, dueOn, note, list) under `UpdatedAt`/`UpdatedBy`, done-state (`IsDone`) under `DoneAt`/`DoneBy`. Reprioritising while someone else ticks it off keeps both changes
  - `Title` is free text, not a catalog lookup. No learning, no autocomplete, no normalization beyond trimming
- [x] `Priority` enum in `Allo.Shared`: `high`, `normal`, `low`, defaulting to `normal`
  - Stored as the string code like `Unit`, never the integer, so the data stays readable and adding a level later never renumbers existing rows
  - Sorted by an explicit rank, not by enum order, since the stored value is a string
  - Priority is to tasks what category is to groceries: the thing that groups and orders the screen
  - **Priority always outranks the due date, confirmed in use (2026-09-24) by the person who asked for the feature:** *"Fires get put out and leaky buckets get plugged, regardless of previous assigned dates."* So a low-priority task overdue by a week still sorts below a high-priority one due next year. This was a genuine fork — sorting by date first is defensible and plenty of task apps do it — so treat it as decided, not as an oversight to fix
- [x] `DueOn` as a **date, not a timestamp** (`DateOnly?`)
  - The app stores UTC. An evening due-time in UTC displays as the previous day depending on where you are, and a household task has no business carrying a timezone. A date also maps cleanly to an all-day calendar event
  - Overdue rows get a red pill, today's a green one, anything further out is plain — a date earns colour only when it matters
  - Verified in the database: a task stores `high 2026-10-01` — the priority code, and a date with no time and no offset, so nothing can shift a due date across midnight
- [x] Several task lists from the start (House, Garden, Errands…). The picker at the top of Tasks always shows, with "+ New list…" at the bottom of it and an edit menu beside it for rename and delete. A new list becomes the current one; the last list cannot be deleted, since the screen would have nowhere to put anything
  - **Missed on the first pass and caught in use (2026-09-24):** the model, sync and `TaskActions` were built and tested, but nothing in the UI called them, and the picker only rendered once a second list existed — so a second list could never be made. A control that hides until it's needed can make its own feature unreachable
  - Picking "+ New list…" then cancelling must not leave the picker reading "+ New list…" over the list you're really on. MudSelect takes the value into its own state before asking the page, so the picker is rebuilt after that choice
  - **Don't set `ToStringFunc` on that picker.** It looks like the obvious way to guarantee it shows a name, but MudSelect calls it before it can find the list and keeps the blank: the picker then read empty on every load. Tried and reverted 2026-09-24
  - **The GUID in the picker, found and fixed (0.2.2).** AddTasks seeded the default "Tasks" list with `HasData`, which stamps the seed sequence (1). On a live install every phone had already synced past 1, so "everything after N" never included the list: its tasks arrived — they were stamped when created — but the list did not. The picker had only the list's id to show, and the edit menu read "Rename" with no name and did nothing. Fresh browsers pull from zero, which is why it would not reproduce in testing. `ResendSeededTaskList` re-stamps the row from the counter so every phone gets it on its next sync; `UpgradeTests` builds a database at the pre-tasks schema, advances the counter as a live install would have, upgrades it, and checks the list reaches a phone that synced before
- [x] Tasks screen: its own sidebar entry, grouped by priority, with a "Done" section at the bottom reusing the In-the-cart pattern
- [x] Reuse the add bar: type a title, press enter, keep typing. Priority and due date are set in an edit sheet, not in the add flow — adding stays one gesture, with a "set priority or date" shortcut on the last thing added
- [x] **Add to calendar.** A task with a due date offers a button that hands the phone a generated `.ics`
  - Generated on the device, so it works offline and assumes nothing about anyone's calendar provider. Not a Google Calendar link, which needs a network and assumes Google
  - An all-day `VEVENT`. `DTEND` is **exclusive** in iCalendar, so a one-day event ends the following day — off by one and the task lands on the wrong date
  - The `UID` is keyed on the task, so adding the same task twice updates the event rather than making a second one in calendars that honour it
  - Delimiters in a title are escaped and long lines folded at 75 octets on character boundaries, because a malformed `.ics` fails silently in a calendar app rather than complaining
  - **The CSP turned out to allow it.** A `blob:` download under `default-src 'self'` raised no violation, verified in a browser, so no policy change was needed
- [x] Guardrail: a task list query never returns a shopping list and vice versa, and a task cannot be parked on a shopping list id (`TaskSyncTests`). Also covered: deletes final, done-group independence, who-ticked-it from the login not the payload, and the offline paths in `TaskOfflineTests`
- [x] Guardrails now the screen exists (`TaskViewTests`): a due date never outranks priority, inside a group the soonest due leads and undated sorts last, a task with no due date is never overdue and neither is a finished one, and the due wording ("Today", "Tomorrow", "Overdue · Sep 19") is pinned

**Deliberately not in scope**, so the screen stays a task list rather than a project manager:

- Assignment to a family member. Tasks already record who added and who completed, same as grocery entries — start there and see if explicit assignment is actually missed
- Reminders or push notifications. Web Push needs VAPID keys, a push service, service-worker push handlers and permission prompts, with unreliable iOS support. The calendar export covers "tell me later" at a fraction of the cost
- Recurring tasks, subtasks, dependencies

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
- [x] Verified over the internet against the live instance: network off, deep route `/stores` reloaded from cache in 646ms. **Still to do by hand: a real phone, in a real store.**
- [x] Split checked state into its own resolution field set: `IsChecked`, `CheckedAt`, `CheckedBy`, resolved independently of the `UpdatedAt` that covers quantity, unit, note, category, and tags. Two resolution rules per entry, not one. Handles the real collision: she changes milk 1 to 2 while you check milk off, both offline. Plain row-level LWW loses one of those changes. Do this before building sync, not after (retrofitting means a migration and a protocol change)
- [x] Server-side monotonic sequence counter instead of timestamps for the sync cursor. Every accepted change gets the next number; client cursor is that number, pull is "everything with seq > N". Immune to phone clock drift, which otherwise lets a device that is 10 minutes fast win every conflict for 10 minutes. SQLite has no `rowversion`, so use an AUTOINCREMENT change-log table or a single counter row

## PWA & Mobile UX

- [x] Web manifest, icons, splash, standalone display mode (`standalone`, maskable 192/512 icons, dark `theme_color`)
- [x] Service worker caching the app shell, with a working update path (no stale-forever builds)
  - A new build installs in the background and **waits**. A snackbar offers it; tapping Update swaps the worker and reloads. Ignoring it means the new build loads at the next cold start anyway. Nothing ever reloads unasked — that would be at its worst mid-aisle
  - The worker records the hash of every asset it cached. The next build carries over the entries that did not change instead of refetching them: measured, a cold install fetches 118 `_framework` files and an update that touched one stylesheet fetched **0**
  - `/api` and `/healthz` are never cached. A stale sync pull or auth check is worse than an honest failure, which the sync queue already retries
  - A failed fetch fails the install. A half-cached shell would serve a broken app offline and never correct itself
- [x] Trim and Brotli-compress the WASM payload, measure cold start on cell data
  - `MapStaticAssets` replaces `UseBlazorFrameworkFiles`/`UseStaticFiles`. The build already writes `.br` copies and their hashes; only this serves them, with `immutable` caching on fingerprinted assets. **12.1MB raw → 3.6MB over the wire**, and nothing after the first load
  - `OverrideHtmlAssetPlaceholders` had to go: the SDK only rewrites `index.html` for a *standalone* client publish. Allo.Api hosts the client, so a published `index.html` kept the literal `#[.{fingerprint}]` and the app would not have booted in production while working fine in dev. `PwaTests` guards this
  - The `wasm-tools` relink is done in the container build (see Deployment). **Measured, it saves almost nothing**: `dotnet.native.wasm.br` 0.93MB → 0.89MB, and the whole `_framework` payload 3.45MB → 3.38MB. Blazor's default publish already trims the IL hard; the large savings quoted for `wasm-tools` come from AOT or from feature switches like invariant globalization, which was turned down deliberately
- [x] Touch targets sized for one-handed use in a store, checkbox hit area generous (48px rows, large checkboxes, add bar at the bottom within thumb reach)
- [x] Keep the keyboard up when adding multiple items in a row
- [x] Keep the add bar visible when the keyboard is open, so the list scrolls behind it instead of the user hunting for the field (reported from real use, 2026-09-23). Fixed with `interactive-widget=resizes-content` on the viewport meta: Android Chrome otherwise leaves the layout viewport full height, so the bar's `bottom: 0` sits *behind* the keyboard. The CSS was already right; the browser just had to be told
- [ ] iOS fallback for the same thing: Safari ignores `interactive-widget`, so it needs a `visualViewport` listener writing the offset to a CSS variable. Not built — no iPhones in the family yet, and it cannot be tested without one
- [x] Dark theme (the example app is dark and it is the right call for a store)
- [x] Manifest and icons verified installable on localhost (secure context), shell served from cache with the network off, including a deep route
- [x] Installed on Android from Chrome and working: real WebAPK, correct icon, standalone with no address bar. Verified beforehand over the real certificate too — secure context, worker active, 75 shell entries cached, manifest `standalone` with both maskable icons resolving
  - **Tell the family to install from Chrome.** Firefox for Android makes a plain launcher shortcut instead of a WebAPK: it works, but the icon comes out blank, and its "App info" points at Firefox, so it is removed with Remove rather than uninstalled. Nothing to do with our icons — they measure correct (opaque, artwork at 62% of the canvas against an 80% safe zone, right MIME type)

## Auth

- [x] Cookie auth, same pattern as EWD ERP: cookie middleware + standalone `PasswordHasher<TUser>`, simple `Users` table. Not full ASP.NET Core Identity, not an external provider
  - Credentials live in a server-only `UserLogins` table (username, hash, `MustChangePassword`), apart from the shared `User` model, so a hash can never be serialized to a client
  - The cookie is re-checked against `UserLogins` on every request, so a cookie for a missing account gets a clean 401
  - Cookie encryption keys persist to `appdata/keys` (`DataProtection__KeysPath`), or every container update would log everyone out
- [x] Long expiry with sliding renewal (nobody should be logged out mid-shop): 1 year sliding, persistent cookie
- [x] `[Authorize]` on all API endpoints as the real boundary; `AuthorizeView` gates the UI (`/api` group requires auth; login/logout opt out. Client: `[Authorize]` on every page via `_Imports.razor`, login opts out)
- [x] Family-scale only: 4 or 5 accounts, no roles, no public sign-up. Accounts created manually or by a simple invite token
  - First account from `Admin__Username` / `Admin__InitialPassword` / `Admin__DisplayName` when none exist; after that any member adds others on the Family page with a temporary password
  - Temporary and initial passwords must be changed at first login. Enforced by the **API**, not just the UI: `TemporaryPasswordGate` 403s every `/api` endpoint except reading your own account, changing your password and logging out. The client routes to Account and skips syncing until it is done
  - Dev bootstrap in `appsettings.Development.json`: `dev` / `allo-dev-pass`
- [x] Change own password, rename display name
- [x] Login rate limiting: 5 attempts per minute per client address, 429 after that
- [x] Client remembers the logged-in user on the device, so the app opens with no signal; only a real 401 logs out
- [ ] Remove a family member (not built; would need a tombstone rather than a delete, since entries reference users)
- [x] Confirm the offline flow behaves when the cookie has expired (do not silently discard queued changes): a 401 during sync sends the user to log in and keeps the queue, which goes up after login
- [x] Re-check the expired-cookie flow once the service worker caches the app shell (PWA section) — the worry was that a cached shell would let the app open with a dead cookie and quietly drop queued changes. It cannot: the worker never caches `/api` (verified against the live instance, 75 cached entries and none of them API), so a 401 still reaches the app, still sets `NeedsLogin`, and the queue still survives in local storage as `SyncEngineTests` covers
- [x] Logging out with unsynced changes warns, and keeps them on the device for the next login

## Deployment

- [x] Multi-stage Dockerfile, one image, API serves the WASM output
  - `wasm-tools` installed in the build stage, which also needs `python3`: Emscripten drives the relink through `emcc`, a Python script, and the SDK image ships no Python (`unable to find python in $PATH` at publish). Kept deliberately after measuring — it only saves ~40KB over the wire and roughly doubles the build time, but the build time is CI's to spend
  - Runs as root, matching EWD ERP, so a bind-mounted appdata share on Unraid needs no `chown`
  - `curl` is installed solely for `HEALTHCHECK`; the aspnet image ships neither curl nor wget. `/healthz` checks the database, so unhealthy means more than "process alive"
  - **Not built or run yet — there is no Docker on the dev machine.** The first real build is CI's
- [x] GitHub Actions: test, then build; publish to GHCR (`ghcr.io/jamesakidd/allo:vX.Y.Z`, `:X.Y` + `latest`) on version tags only
  - Also builds (without publishing) on every push to master, because this repo merges straight to master and never opens PRs — otherwise a broken Dockerfile would first surface on a release tag
  - The image build depends on the test job, so a failing suite can never publish an image
  - Pin a version tag on the Unraid container rather than tracking `latest` (the lesson from EWD ERP QA)
  - **The image tag has no `v`.** `metadata-action` strips it, so a `v0.1.0` git tag publishes `ghcr.io/jamesakidd/allo:0.1.0`, `:0.1` and `:latest`. `:v0.1.0` does not exist and pulling it 404s
  - First release published 2026-09-22 as `0.1.0`; the package is public, so Unraid pulls it with no `docker login`
- [x] Unraid container template, all config via env vars (double-underscore keys) — `deploy/allo.xml`, `br0` with its own IP like `EWDERP_QA`, port 8080, one `/appdata` volume. Unraid pulls a prebuilt image and never builds from source; the "Repository" field on its form is an *image* repo, not a git repo
- [x] Make the GHCR package public after the first publish, so Unraid pulls with no login (the ERP's private package needs a `docker login` that does not survive a reboot, since Unraid's rootfs is RAM-backed)
- [x] **First deploy, 2026-09-22: running on Unraid on its own br0 address, port 8080, image `0.1.2`.** Verified against the live container: security headers present, Brotli served (`dotnet.native.wasm` 2.77MB → 932KB on the wire), `/api` 401s without a login, and a container restart kept both the list *and* the session — so the database and the data protection keys are genuinely on the bind mount, which is the failure that would otherwise stay invisible until the first update
- [x] SQLite file on a bind-mounted appdata volume — one `/appdata` volume holds the database *and* the data protection keys, since losing the keys logs the whole family out
- [x] NPM reverse proxy with a valid Let's Encrypt cert (required for service worker and home screen install; do not rely on Tailscale, family members will not have the tailnet up in a store)
  - Live behind NPM on its own subdomain → the container's `:8080`. Force SSL, HTTP/2, HSTS (`max-age=63072000; preload`), websockets off, NPM asset caching off so it can never serve a stale `service-worker.js` or `index.html`
  - Moved off the original dynamic-DNS hostname on 2026-09-23, onto a wildcard cert issued by DNS-01. A PWA's cache, install and unsynced queue are bound to the **origin**, so moving hostname means reinstalling on every device — done while only one phone had it. Don't move it again casually, and don't redirect the old name: an old install's `/api` calls would follow the redirect to an origin its cookie does not cover
  - Re-verified on the new hostname: wildcard cert, 301 from http, HSTS, CSP, Brotli (`dotnet.native.wasm` 932KB), secure context, worker active with 75 cached entries, deep route reloading offline in 601ms
  - A second family now needs only a container and an NPM host — the wildcard already covers it, so there is no cert request per family
  - **NPM does not persist Force SSL / HSTS when they are set in the Add dialog, before the certificate exists.** Both silently did nothing until the host was re-saved with the issued cert selected. Symptom: port 80 served the app instead of redirecting, and no HSTS header
  - Verified from outside: 301 to https, HSTS present, Brotli intact through the proxy (`dotnet.native.wasm` 932KB), and the auth cookie now comes back `secure` — which only happens when `X-Forwarded-Proto` is read from a trusted proxy, so forwarded headers are confirmed working and the rate limiter partitions by real client address
- [x] Backup: scheduled copy of the SQLite file, plus a manual copy before any container update — `deploy/backup-allo.sh`, for the User Scripts plugin, 30-day retention
  - **Installed and running daily as of 2026-09-23**, as User Script "Backup Allo" → `/mnt/user/backups/allo/allo_<stamp>.tar.gz` (~80KB). The first run printed no keys warning, so the keys are in the archive
  - Each run stops the container for a few seconds. Harmless even mid-shop, since the phone works offline and syncs after — but schedule it for an hour nobody shops
  - Archives the database **and** the data protection keys together. Restoring a database without its keys leaves everyone logged out with an undecryptable cookie
  - Stops the container for the copy. SQLite is a file, not a server: a copy taken mid-write can be torn, and the tear is silent until restore time. A few seconds of downtime for a shopping list is the cheap side of that trade, and it needs no `sqlite3` on the host or in the image
- [x] Forwarded headers for NPM: trust `X-Forwarded-For`/`-Proto` from the proxy only (`KnownProxies`). Without it the login rate limiter sees one address (the proxy) for everyone, and the cookie's `SameAsRequest` secure flag sees plain http
  - Set `ForwardedHeaders__KnownProxies__0` to NPM's address. An empty list leaves the middleware out of the pipeline entirely, which is what a direct LAN run wants
  - **Never seed a synced row in a migration that ships after installs exist.** `HasData` stamps a fixed sequence and cannot know the live counter, so every existing phone — already synced past it — never receives the row. It only shows on an upgraded install; a fresh one pulls from zero and looks fine. Create such rows at startup through `SaveChanges`, which stamps from the counter, or re-stamp them from the counter in the migration. Test it with `TestDatabase(migrateTo: ...)` — see `UpgradeTests`
  - **A container passes a variable the user left blank as an empty string, not as absent.** `IPAddress.Parse("")` threw and the container would not start at all (first real deploy, 2026-09-22). Every setting read from config must treat blank as unset; `ContainerConfigTests` starts the app with the whole template blank
  - The framework's default trusted networks are cleared: honouring `X-Forwarded-For` from anyone would let a caller claim any address and walk around the login rate limit. `ForwardedHeaderTests` pins this
- [x] Security review for internet exposure, same considerations as EWD ERP

**Fixed by the review:**
- **Dev credentials shipped inside the image.** `appsettings.Development.json` (`dev` / `allo-dev-pass`) was copied into the publish output. Production never loads it, but the GHCR package is public, so anyone could read it — and starting the container with `ASPNETCORE_ENVIRONMENT=Development` would create that account for real. Now excluded from publish
- **`MustChangePassword` was UI-only.** A temporary password read out over the phone kept full API access for as long as nobody visited the Account page. Now enforced server-side (see Auth). Logging out is deliberately still allowed
- **No security headers.** Added CSP, `X-Content-Type-Options`, `Referrer-Policy`, in the app rather than on the proxy so they survive someone rebuilding the proxy entry. The CSP is the minimum Blazor WASM needs — `'wasm-unsafe-eval'`, blob: workers, and inline *styles* for MudBlazor's popovers — but **no `'unsafe-inline'` for scripts**, which is why the inline service-worker registration moved into `app-update.js`. Verified in a browser: no violations

**Reviewed and accepted, not bugs:**
- Login is rate limited (5/min/address) and answers unknown usernames with a dummy hash verify, so neither status codes nor timing reveal which accounts exist
- Sync takes identity from the login and never from the payload; all `/api` requires auth; unmatched `/api` routes 404 rather than falling through to `index.html`
- Any member can add another member. Deliberate (no roles, family scale), but it means one compromised account can create a lasting second one. Revisit if "remove a family member" gets built
- `/healthz` is anonymous, which the container's HEALTHCHECK needs. It reveals only that an app is up
- `AllowedHosts` is `*`. The app builds no absolute URLs from `Host`, so there is nothing to poison

**Left to the proxy:** HSTS belongs on NPM, which terminates TLS. The app deliberately does not send it, since it also serves plain http on the LAN.

## Misc / Cosmetic

- [x] App icon and favicon (shipped with the logo work: favicon, apple-touch-icon, maskable 192/512)
- [x] Docker logo for the Unraid dockers page — the template points at `icon-192.png` on raw.githubusercontent.com
- [x] Empty state for a fresh list

## Parked / Later

- [ ] Import: paste a block of text, one item per line, bulk-add with categorization suggestions. **Cancelled 2026-09-23** — it existed to make the move off Flipp painless, and that list turned out small enough to type in by hand. Revive only if a bulk paste is wanted for its own sake
- [ ] Real-time push via SignalR (only if focus-refresh proves insufficient)
- [ ] Price history per item per store, manually entered
- [ ] Recurring staples with a suggested cadence
- [ ] Barcode scan to add (browser camera API, accuracy will be the problem)
- [ ] Meal planning that generates list entries
- [ ] some kind of way of enabling a desktop UI
