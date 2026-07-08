# Story: The vault: auto-save every completed reveal server-side

**Feature:** Keepsake Vault  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
Today, a finished tale's ONLY durable copy is the device-local IndexedDB
gallery (`keepsake-gallery/03`, `web/src/gallery/localGallery.ts`): a 30-tale
cap with silent oldest-first eviction, save failures swallowed, and no server
copy of any kind. `api/src/Rooms/Room.cs`'s header comment is explicit that a
room "lives only in the memory of the in-process SignalR hub for the duration
of a play session" - once the room is gone, nothing server-side remembers the
tale ever existed. ADR 0003's audit named this the unanswerable support
question ("where are my saved stories?"). This story is the foundation Decision
2 describes: an anonymous, server-side keepsake vault, keyed by a device-held
random vault id, that every completed reveal auto-saves into. See
[feature.md](./feature.md) and
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) (Decision 2,
Layer 2).

## Acceptance Criteria
- [ ] AC-01: Given a device with no existing vault id, when it needs one (first
      reveal, or app load), then the client mints a random vault id and
      persists it durably on-device (`localStorage`), reusing the SAME id for
      every future save from that device - mirroring the opaque,
      cryptographically random, never-identity-derived handle pattern
      `api/src/Rooms/Room.cs`'s `NewReconnectToken()` already establishes
      (`RandomNumberGenerator`/`crypto.randomUUID`, never a sequential or
      derived value).
- [ ] AC-02: Given a completed reveal on a device holding a vault id, when the
      reveal finishes, then the client auto-saves the tale (title, ordered
      parts, byline nicknames, `createdUtc`) to the vault as a fire-and-forget
      call - the SAME already-assembled, already-filtered content shape
      `CloudGalleryController`'s save endpoint already accepts (`Title`,
      `Parts` as ordered `{IsWord, Text}` runs, `BylineNames`) - and a
      network/store failure never blocks, delays, or visibly degrades the
      reveal screen (mirrors `Reveal.tsx`'s existing `saveTale()` fire-and-
      forget call at `handleSaveImage`, ~line 972).
- [ ] AC-03: Given an unclaimed vault (no family account has claimed it - see
      story 03), then its stored tales expire on a TTL - **default 90 days**
      from the tale's `CreatedUtc` - implemented as lazy expiry-on-read
      (mirroring `PublishedTale.IsExpired`/`TableStoragePublishedTaleStore.GetAsync`'s
      posture: an expired tale reads as gone, with a best-effort delete to
      reclaim the row). The 90-day figure is a settings-key candidate
      (`control-plane`'s "knob migration" list) - ship it as a code constant
      default until `control-plane/01`'s catalog exists; do not block this
      story on `control-plane`.
- [ ] AC-04 (child-safety / no PII): Given any tale saved to the vault, then
      the server re-vets EVERY non-empty part (coral player-words AND
      "literal" template runs) plus the byline through the authoritative
      `IContentSafetyFilter` before storing - the client's word/literal
      classification is NOT trusted, exactly the posture
      `CloudGalleryController.Save`/`PublishedTalesController`'s publish path
      already enforce - and any failure rejects the whole save. The vault id
      is a random handle: it is never derived from, or joined to, an email,
      device fingerprint, IP address, or any other identity. Nothing new
      lands on `Room` or `Player` (`api/src/Rooms/Room.cs` is not touched by
      this story).
- [ ] AC-05: Given no Table Storage connection string is configured (local
      dev, CI, a fresh clone), then a genuinely WORKING in-memory vault store
      is registered instead - mirroring `ICloudGalleryStore`'s
      `InMemoryCloudGalleryStore` split (a real, thread-safe fallback, NOT
      `DisabledPublishedTaleStore`'s no-op), so the whole save/list flow this
      and later stories build on is exercisable with zero Azure setup.
- [ ] AC-06: Given the vault's write endpoint (an anonymous, unauthenticated
      surface reachable by every device), then it is rate-limited per client
      IP - mirroring `CloudGalleryRateLimit`/`PublishTalesRateLimit`'s
      fixed-window pattern - so a scripted/abusive caller cannot flood the
      store.

## Out of Scope
- Claiming a vault into a family account, or claim-code recovery -
  `keepsake-vault/03`.
- Merging the vault into the "Tales we've carved" gallery screen's own read
  path - `keepsake-vault/02`. This story only builds the server-side vault
  and the auto-save write call; it adds no new browsing UI.
- Soft-delete / restore semantics - `keepsake-vault/04`. A tale this story
  saves is only ever hard-removed by its TTL expiry in this story's scope.
- Any change to `keepsake-gallery/01`'s render function, `03`'s local
  IndexedDB gallery, or `05`'s cloud-sync gallery - this story adds a NEW,
  independent write path alongside them; it does not edit their code.
- Migrating existing local-gallery tales (saved before this story shipped)
  into the vault retroactively - a device's vault starts empty; only reveals
  completed after this story ships are auto-saved server-side.

## Technical Notes
- **New `api/src/Vault/` namespace**, isolated from `GameHub.cs`/`api/src/Rooms/`
  exactly like `api/src/CloudGallery/` and `api/src/PublishedTales/` already
  are (neither imports from `Rooms`; neither touches the hub or the round
  lifecycle) - this story follows the same isolation precedent:
  - `VaultTalePart.cs` / `VaultTale.cs`: the stored record - mirror
    `CloudTalePart`/`CloudTale` exactly (`OwnerKey`-equivalent is the vault id
    here, not an account-email hash; `TaleId`, `Title`, `Parts`, `BylineNames`,
    `CreatedUtc`). Keep it a LOCAL record type (do not import
    `CloudGallery.CloudTale` or `PublishedTales.TalePart`) per the isolation
    precedent both existing keepsake stores already set - the features share a
    content SHAPE, not a type.
  - `IVaultStore.cs`: `SaveAsync(VaultTale)`, `ListAsync(vaultId)`,
    `IsEnabled` - mirror `ICloudGalleryStore`'s contract shape.
  - `TableStorageVaultStore.cs`: Azure Table Storage, `PartitionKey = vaultId`,
    `RowKey` = a minted tale id (reuse `PublishedTales.SlugGenerator.Generate()`
    as the id minter, the same deliberate shared-minter reuse
    `CloudGalleryController` already makes - not a data-model coupling).
    Parts serialize to one `PartsJson` string property, mirroring both
    existing stores. Apply the TTL (AC-03) as lazy expiry-on-read, mirroring
    `TableStoragePublishedTaleStore.GetAsync`'s pattern exactly (a
    `CreatedUtc + TtlDays` computation rather than a stored `ExpiresUtc`, so a
    later TTL setting change - once `control-plane` exists - applies to
    existing rows without a migration).
  - `InMemoryVaultStore.cs`: a WORKING thread-safe fallback, mirroring
    `InMemoryCloudGalleryStore` (not a no-op).
  - `VaultRateLimit.cs`: mirror `CloudGalleryRateLimit`/`PublishTalesRateLimit`'s
    per-IP fixed-window policy (`ASP.NET Core`'s built-in rate limiter, no new
    dependency).
  - `VaultController.cs`: `POST /api/vault/{vaultId}/tales` (auto-save, rate
    limited) and `GET /api/vault/{vaultId}/tales` (read - consumed by story
    02). No auth: a vault id IS the credential (anonymous by construction,
    exactly like a room join code is the credential to a room) - anyone
    holding the id can read/write it, which is the deliberate, minimal-friction
    design (mirrors a join code's trust model, not a purchaser credential's).
- **`Program.cs` registration**: connection-string-gated, exactly like
  `CloudGallery:StorageConnectionString`/`PublishedTales:StorageConnectionString`
  (`Vault:StorageConnectionString`). **This is the ADR 0003 wave-1 hotspot**:
  this story's `Program.cs` edit must land as its own small, rebased PR and
  merge SERIALLY with the other wave-1 stories that also touch `Program.cs`
  (`accounts-identity/05`, `control-plane/01`, `sysadmin-console/04`) - see
  this feature's `implementation.md` Wave Plan. Do not batch these edits.
- **Web**: a new `web/src/vault/vaultId.ts` (mint-or-read the durable
  `localStorage` vault id, mirroring `localGallery.ts`'s `generateTaleId()`
  fallback posture for environments without `crypto.randomUUID`) and
  `web/src/vault/vaultClient.ts` (the fire-and-forget POST, mirroring
  `web/src/gallery/publishTale.ts`'s "endpoint call, graceful failure, no
  throw" shape). `Reveal.tsx`'s `handleSaveImage` (~line 943-980) gains ONE
  more fire-and-forget call alongside its existing `saveTale()` call - same
  posture, same place, never blocking the download.
- **Config**: `VITE_*` for the API base URL (already established,
  `web/src/vite-env.d.ts`) - no new env surface.

## Tests
| AC | Test |
|---|---|
| AC-01 | unit (Vitest): vault-id mint/read is idempotent (a second call returns the same stored id); falls back gracefully without `crypto.randomUUID` |
| AC-02 | unit (Vitest, mocked fetch) + xUnit: the save call fires without awaiting the reveal flow; a rejected/slow network call does not delay the caller |
| AC-03 | xUnit: `TableStorageVaultStore`'s TTL-expiry helper (pure, given a `CreatedUtc` and `now`) returns expired past the configured TTL; a listed vault omits expired tales |
| AC-04 | xUnit: `VaultController.Save` rejects an unsafe part/byline regardless of client `IsWord` classification (mirrors `PublishedTalesControllerTests`'s CR-001 regression); code review confirms no identity field ever reaches `VaultTale` |
| AC-05 | xUnit: with no connection string configured, `InMemoryVaultStore` is registered and a save -> list round-trip succeeds |
| AC-06 | xUnit: `VaultRateLimit` partitions per client IP and returns 429 once the limit is exceeded (mirrors `PublishTalesRateLimitTests`) |

## Dependencies
none (foundation story). Cross-feature note: this story's `Program.cs` edit
must merge serially alongside `accounts-identity/05`, `control-plane/01`, and
`sysadmin-console/04` - all four are ADR 0003 wave-1 stories that touch
`Program.cs` - see `implementation.md`.
