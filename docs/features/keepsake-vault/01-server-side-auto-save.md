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
- [ ] AC-01 (handle-is-a-secret, entropy floor): Given a device with no
      existing vault id, when it needs one (first reveal, or app load), then
      the client mints the vault id using `crypto.randomUUID()` ONLY and
      persists it durably on-device (`localStorage`), reusing the SAME id for
      every future save from that device - mirroring the opaque,
      cryptographically random, never-identity-derived handle pattern
      `api/src/Rooms/Room.cs`'s `NewReconnectToken()` already establishes.
      There is NO client-side `Math.random`-style fallback for environments
      without `crypto.randomUUID` - a device that lacks it instead calls a
      new, unauthenticated `POST /api/vault/mint` endpoint (no body) that
      returns a fresh, server-minted, `RandomNumberGenerator`-backed vault id
      (mirroring `Room.NewReconnectToken()`'s server-side primitive). The
      server independently enforces a minimum length/format floor on any
      client-presented vault id on every vault endpoint (reject with 400
      anything shorter than a UUID's 36 characters or failing a basic
      random-looking-token shape check) - a weak, client-forged id is never
      accepted as a bearer credential regardless of what the client sent.
- [ ] AC-02 (bearer credential, never in the URL): Given a completed reveal on
      a device holding a vault id, when the reveal finishes, then the client
      auto-saves the tale (title, ordered parts, byline nicknames - NOT
      `createdUtc`, see below) to the vault as a fire-and-forget call - the
      SAME already-assembled, already-filtered content shape
      `CloudGalleryController`'s save endpoint already accepts (`Title`,
      `Parts` as ordered `{IsWord, Text}` runs, `BylineNames`) - and a
      network/store failure never blocks, delays, or visibly degrades the
      reveal screen (mirrors `Reveal.tsx`'s existing `saveTale()` fire-and-
      forget call at `handleSaveImage`, ~line 972). The vault id is carried in
      an `X-Vault-Id` request HEADER on every vault call, NEVER as a URL path
      segment or query parameter: `PiiScrubbingTelemetryInitializer` only
      strips the query string, so a path segment would leak the credential to
      App Insights, access logs, `Referer`, and browser history. `CreatedUtc`
      is SERVER-STAMPED (`DateTimeOffset.UtcNow` at write time, exactly like
      `CloudGalleryController.Save`'s existing `CreatedUtc: DateTimeOffset.UtcNow`)
      and is never accepted from the client request body - this endpoint is
      anonymous and abusable, and the TTL (AC-03) keys off `CreatedUtc`, so a
      client-supplied timestamp would be directly spoofable to defeat expiry.
- [ ] AC-03 (TTL, computed not stored): Given an unclaimed vault (no family
      account has claimed it - see story 03), then its stored tales expire on
      a TTL - **default 90 days** from the tale's server-stamped `CreatedUtc`
      - computed as `CreatedUtc + TtlDays` AT READ TIME (no `ExpiresUtc`
      column is stored). Expiry is applied on the LIST/partition-query path
      (`ListAsync(vaultId)`, which returns every tale for that vault id in one
      query) - each row's computed expiry is checked as the partition is
      enumerated, with a best-effort per-row delete for any row found expired,
      rather than a single-row `GetAsync`-shaped lookup (the vault has no
      single-slug read path; every read is a partition list). This is
      deliberately NOT an exact mirror of
      `TableStoragePublishedTaleStore.GetAsync`, which stores an `ExpiresUtc`
      column and checks a single row by slug - that shape fits a one-row
      public-link lookup, not this feature's per-vault list. The 90-day figure
      is a settings-key candidate (see the Technical Notes' control-plane
      note) - ship it as a code constant default until `control-plane/01`'s
      catalog exists; do not block this story on `control-plane`.
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
      this story). Byline nicknames stored in the vault remain PLAY-PLANE
      content (no account linkage) until a family claims the vault
      (`keepsake-vault/03`) - only from that point on do they become
      account-plane/household data under ADR 0003's carve-out (an adult
      claimed it), never before.
- [ ] AC-05: Given no Table Storage connection string is configured (local
      dev, CI, a fresh clone), then a genuinely WORKING in-memory vault store
      is registered instead - mirroring `ICloudGalleryStore`'s
      `InMemoryCloudGalleryStore` split (a real, thread-safe fallback, NOT
      `DisabledPublishedTaleStore`'s no-op), so the whole save/list flow this
      and later stories build on is exercisable with zero Azure setup.
- [ ] AC-06 (rate limit, read AND write): Given the vault's endpoints (an
      anonymous, unauthenticated-by-design surface reachable by every device),
      then BOTH the write (`POST /api/vault/tales`) and the READ
      (`GET /api/vault/tales`) endpoints are rate-limited per client IP -
      mirroring `CloudGalleryRateLimit`/`PublishTalesRateLimit`'s fixed-window
      pattern - so a scripted/abusive caller cannot flood the store OR
      enumerate/scrape reads. (Previous drafts of this story rate-limited the
      write endpoint only - that gap is closed here.)
- [ ] AC-07 (storage-bloat bound): Given a single vault id, then it is capped
      at **`MaxTalesPerVault` = 500** stored tales; a save that would push a
      vault past this cap is rejected (the client's fire-and-forget call
      simply fails silently, matching AC-02's never-block posture - a family
      never notices in normal use since 500 tales is far beyond organic play
      volume within a 90-day TTL window). This bounds per-vault storage
      growth independent of the per-IP rate limiter (AC-06), which alone is
      defeated by an attacker rotating source IPs against ONE vault id - the
      per-vault cap holds regardless of how many IPs the writes come from.

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
    existing stores. Apply the TTL (AC-03) as a COMPUTED
    `CreatedUtc + TtlDays` check performed while enumerating the
    `PartitionKey = vaultId` query in `ListAsync` (no stored `ExpiresUtc`
    column) - each expired row found during that enumeration is filtered from
    the result and best-effort-deleted to reclaim it. Do NOT mirror
    `TableStoragePublishedTaleStore.GetAsync` here: that method stores
    `ExpiresUtc` and checks one row by slug, a shape built for a single-link
    public read, not this feature's per-vault partition list - citing it as an
    "exact mirror" (an earlier draft of this story did) is wrong and has been
    corrected.
  - `InMemoryVaultStore.cs`: a WORKING thread-safe fallback, mirroring
    `InMemoryCloudGalleryStore` (not a no-op). Also enforces the
    `MaxTalesPerVault` cap (AC-07) identically to the Table Storage store, so
    local dev/CI exercises the same bound.
  - `VaultRateLimit.cs`: mirror `CloudGalleryRateLimit`/`PublishTalesRateLimit`'s
    per-IP fixed-window policy (`ASP.NET Core`'s built-in rate limiter, no new
    dependency) - applied to BOTH the read and write endpoints (AC-06); the
    write policy may use a tighter window than the read policy if desired, but
    both must carry `[EnableRateLimiting]`.
  - `VaultController.cs`: `POST /api/vault/tales` (auto-save, rate limited)
    and `GET /api/vault/tales` (read, ALSO rate limited per AC-06 - consumed
    by story 02); both read the vault id from the `X-Vault-Id` request HEADER,
    never a route parameter (AC-02) - there is no `{vaultId}` route segment
    anywhere in this controller. A third, unauthenticated
    `POST /api/vault/mint` endpoint (AC-01) returns a fresh server-minted
    vault id for the no-`crypto.randomUUID` fallback path. No further auth: a
    vault id IS the bearer credential (anonymous by construction, exactly like
    a room join code is the credential to a room) - anyone holding the id can
    read/write it (subject to AC-01's server-side length/format floor and
    AC-06's rate limits), which is the deliberate, minimal-friction design
    (mirrors a join code's trust model, not a purchaser credential's).
  - `MaxTalesPerVault = 500` (AC-07): a named constant, checked in `SaveAsync`
    before the write; exceeding it rejects the save (no eviction - the vault
    is a durable archive, not a rolling cache like the local gallery).
- **`Program.cs` registration**: connection-string-gated, exactly like
  `CloudGallery:StorageConnectionString`/`PublishedTales:StorageConnectionString`
  (`Vault:StorageConnectionString`). **This is the ADR 0003 wave-1 hotspot**:
  this story's `Program.cs` edit must land as its own small, rebased PR and
  merge SERIALLY with the other wave-1 stories that also touch `Program.cs`
  (`accounts-identity/05`, `control-plane/01`, `sysadmin-console/04`) - see
  this feature's `implementation.md` Wave Plan. Do not batch these edits.
- **Web**: a new `web/src/vault/vaultId.ts` (mint-or-read the durable
  `localStorage` vault id). Unlike `localGallery.ts`'s `generateTaleId()` -
  which deliberately falls back to a `Math.random`-based string because a
  local gallery row id is not a credential - `vaultId.ts` must NOT copy that
  fallback: the vault id IS a bearer credential (AC-01), so the only accepted
  client-side mint path is `crypto.randomUUID()`; when it is unavailable, call
  `POST /api/vault/mint` for a server-minted id instead of generating a weak
  one locally. `web/src/vault/vaultClient.ts` (the fire-and-forget POST/GET,
  mirroring `web/src/gallery/publishTale.ts`'s "endpoint call, graceful
  failure, no throw" shape) sends the vault id as an `X-Vault-Id` header on
  every call, never interpolated into a URL path. `Reveal.tsx`'s
  `handleSaveImage` (~line 943-980) gains ONE more fire-and-forget call
  alongside its existing `saveTale()` call - same posture, same place, never
  blocking the download.
- **Config**: `VITE_*` for the API base URL (already established,
  `web/src/vite-env.d.ts`) - no new env surface.
- **Control-plane note**: the vault TTL (`TtlDays`, default 90) and
  `MaxTalesPerVault` (default 500) are both settings-key candidates. Until
  `control-plane/01`'s catalog exists, both ship as named code constants with
  the defaults above; the code degrades to those defaults, and this story is
  not blocked on `control-plane` landing first.

## Tests
| AC | Test |
|---|---|
| AC-01 | unit (Vitest): vault-id mint/read is idempotent (a second call returns the same stored id); with `crypto.randomUUID` unavailable, the client calls `/api/vault/mint` rather than generating a `Math.random`-based id; xUnit: `VaultController` rejects a client-presented `X-Vault-Id` shorter than the length floor with 400 |
| AC-02 | unit (Vitest, mocked fetch) + xUnit: the save call fires without awaiting the reveal flow; a rejected/slow network call does not delay the caller; xUnit: `VaultController.Save` ignores/rejects a client-supplied `createdUtc` field and stamps `DateTimeOffset.UtcNow` instead; code review: no controller action reads `vaultId` from a route parameter |
| AC-03 | xUnit: `TableStorageVaultStore`'s TTL-expiry helper (pure, given a `CreatedUtc` and `now`) returns expired past the configured TTL; `ListAsync` omits expired rows found during partition enumeration and best-effort deletes them |
| AC-04 | xUnit: `VaultController.Save` rejects an unsafe part/byline regardless of client `IsWord` classification (mirrors `PublishedTalesControllerTests`'s CR-001 regression); code review confirms no identity field ever reaches `VaultTale` |
| AC-05 | xUnit: with no connection string configured, `InMemoryVaultStore` is registered and a save -> list round-trip succeeds |
| AC-06 | xUnit: `VaultRateLimit` partitions per client IP and returns 429 once the limit is exceeded, on BOTH the `POST` and `GET` endpoints (mirrors `PublishTalesRateLimitTests`) |
| AC-07 | xUnit: a save past `MaxTalesPerVault` for a single vault id is rejected while a save under the cap for a DIFFERENT vault id succeeds |

## Dependencies
none (foundation story). Cross-feature note: this story's `Program.cs` edit
must merge serially alongside `accounts-identity/05`, `control-plane/01`, and
`sysadmin-console/04` - all four are ADR 0003 wave-1 stories that touch
`Program.cs` - see `implementation.md`.
