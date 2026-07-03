# Story: Cloud-synced, browsable gallery for purchasers

**Feature:** Keepsake Gallery  ·  **Status:** Complete  ·  **Issue:** #154

## Context
Story 03's "Tales we've carved" gallery is device-local and anonymous by
design - it disappears the moment browser storage is cleared, and it only
ever shows one device's tales. A purchasing family that plays across a
phone, a tablet, and the family PC will want their saved tales to follow
them, and, once a family has dozens of tales, to browse or search for a
specific one rather than scroll a recency list. This story is that: a
cloud-synced keepsake gallery available to a signed-in **purchaser**, tied to
the purchaser account rather than a device. It was explicitly parked in
`feature.md`'s Out-of-Scope list ("Cloud-synced keepsake gallery tied to a
purchaser account" and "Search/filter within the gallery beyond a simple
recency list") until accounts and entitlements existed - they now do (or are
being built) as their own features, so this story is what picks the idea back
up. See [feature.md](./feature.md) and README section 7 (Phase 2+).

This story is **GATED**: it cannot start until `accounts-identity/02`
(lightweight purchaser account) exists, and it consumes the
`billing-entitlements/01` session-creation entitlement seam rather than
inventing its own check.

## Acceptance Criteria
- [x] AC-01: Given a signed-in purchaser (accounts-identity/02) has tales in
      their device-local gallery (story 03) and uploads them to the cloud from
      the signed-in Account area, when they sign in on a second device, then the
      same tales appear in their cloud gallery there too - the gallery is keyed
      to the purchaser account, not to a device's local storage. (Decision
      2026-07-03: cloud sync is an explicit purchaser-surface action - browse +
      upload from the signed-in Account area - NOT an automatic push from the
      child-facing reveal, which must never touch a purchaser credential per the
      auth-boundary invariant established by accounts-identity/03. The outcome -
      tales follow the purchaser across devices - is unchanged; only the trigger
      is an explicit sync. See Decisions at pickup below.)
- [x] AC-02: Given a player has never signed in as a purchaser (the common,
      anonymous case), when they save tales, then those tales stay in the
      device-local gallery from story 03 only - no cloud gallery is created,
      offered, or implied for them; anonymous players never get a cloud
      gallery, full stop (README section 6 - minimal data on minors; mirrors
      accounts-identity/01's "anonymous, forever" contract).
- [x] AC-03: Given a purchaser's cloud gallery, then they can browse it beyond
      a simple recency list: search by title or byline name, and filter/sort
      (e.g. most-liked, once `the-reveal`'s reaction counts exist, or by
      date/session) - the access pattern this story exists to add, which
      `keepsake-gallery/03`'s local list and `04`'s single-slug lookup do not
      need to support.
- [x] AC-04 (entitlement): Given a signed-in purchaser opens the cloud gallery,
      then whether cloud sync is available is decided once, when the signed-in
      gallery surface is entered (the authenticated gallery-load call), by reading
      the `billing-entitlements/01` seam (`IEntitlementService.EvaluateForSession`)
      for the dedicated capability key `gallery.cloudSync` - not per-save or
      per-item. (Account-scoped, not room-scoped: the "session" here is the
      purchaser's signed-in session, since a cloud gallery is unrelated to a
      room/round - the AI-gate's "at room-creation" framing does not apply.) The
      free tier keeps the device-local gallery (story 03) in full. **Decision
      2026-07-03:** `gallery.cloudSync` ships **default-unlocked** for signed-in
      purchasers (added to the default-unlocked baseline alongside the `ai.*`
      keys), so the effective gate is "is this a signed-in purchaser" (a 401
      without a valid purchaser credential). The seam is still read against the
      catalog key so real gating can later flip it to a stored grant with no
      consumer refactor (mirrors `ai-cost-gate/02`'s default-unlocked posture).
- [x] AC-05 (child-safety): Given any tale synced to or browsed in the cloud
      gallery, then it is the same already-filtered content story 01 already
      produced (no new free-text entry point), and the only identity attached
      to a synced tale is the in-session nickname(s) + Guardian variant(s) -
      never a real name, email, or other PII from the purchaser account leaks
      onto a tale a child might see.
- [x] AC-06: Given a purchaser deletes their account or revokes cloud sync,
      then their cloud-stored tales are removed within a bounded, documented
      window - consistent with "most data is mutable, nothing here is a
      system of record" (README section 4).

## Out of Scope
- The anonymous, device-local gallery itself - that is `keepsake-gallery/03`
  and is unaffected by this story; it remains the free, account-free default
  for every player who never purchases.
- The public, unauthenticated shareable tale link - that is
  `keepsake-gallery/04` and its Azure Table point-read-by-slug design is not
  reopened by this story (see Datastore decision below - it is only the
  point-read pattern that is out of scope for reconsideration, not the whole
  Table Storage choice for every future keepsake concern).
- Building the purchaser account itself (`accounts-identity/02`) or the
  entitlement seam (`billing-entitlements/01`) - this story only consumes
  both once they exist.
- Reaction counts / "most-liked" as a concept - that is `the-reveal`'s own
  parked idea; this story only adds the sort/filter surface for it if and
  when it ships, it does not invent reactions.
- A public, cross-purchaser gallery or any discovery/browse of other
  families' tales - this is a private gallery scoped to one purchaser
  account, not a directory.
- Migrating or importing a device's existing local-gallery (story 03) tales
  into the cloud gallery on first sign-in - a reasonable follow-up, but not
  required for this story to ship; note as a candidate small addition.

## Decisions at pickup (2026-07-03)
Picked up once both hard gates landed on main (accounts-identity/02 #68 code
merged via PR #147; billing-entitlements/01 #70 Complete via PR #152). Three
decisions, each recorded here and in `feature.md`'s Decisions log:
- **Datastore: stay on Azure Table Storage** (see the section below - client-side
  scans over one purchaser's own bounded tale set; no new Azure resource).
- **Entitlement: `gallery.cloudSync` default-unlocked** for signed-in purchasers
  (see AC-04); the effective gate is a valid purchaser credential.
- **Sync is an explicit purchaser-surface action, not an at-reveal push** (see
  AC-01) - the child-facing game/reveal flow never touches a purchaser credential
  (auth-boundary invariant, README section 6 / accounts-identity/03).

## Datastore decision - RESOLVED 2026-07-03: stay on Azure Table Storage
Chosen: **Azure Table Storage**, one entity per synced tale, `PartitionKey` = the
owner key (the SHA-256 hash of `account.Email`, reusing `AccountIdentity.KeyFor`
so the gallery keys exactly like accounts/grants), `RowKey` = a minted tale id.
List-by-owner is a single-partition query; search-by-title/byline and sort are
done client-side over that bounded per-purchaser result set. Rationale: keeps the
5-resource footprint (README section 9), no new ORM/connection pattern, and a
family's tale count stays small enough that partition-scoped scans are cheap. If a
single purchaser's tale count ever grows large enough that client-side
browse/search degrades, revisit Azure SQL/Cosmos then (a documented later trigger,
not a now-cost). The original trade-off analysis is kept below for that revisit.

### Original open analysis (kept for the future revisit trigger)
`keepsake-gallery/04` chose Azure Table Storage because its ONLY access
pattern is a single keyed point-read (`PartitionKey = RowKey = slug`) - cheap,
zero-query, rides the existing storage account. This story introduces access
patterns Table Storage was never chosen for: **list all tales for a given
purchaser, search by title/byline text, and rank/sort by a field like
like-count or date** - querying ACROSS tales and BY OWNER, not a single key
lookup. Table Storage supports OData-ish partition scans and secondary
indexes only awkwardly and at a real latency/cost penalty; a queryable store
(Azure SQL or Cosmos DB) is a more natural fit for browse/search/rank. This is
**a decision to make when this story is picked up, not a decision made now**:
- Azure Table Storage (stay): cheapest, already-provisioned, zero new
  resource - but browse/search/rank become client-side scans over a
  purchaser's own (bounded) tale count, workable only if a single
  purchaser's tale count stays small.
- Azure SQL or Cosmos DB (move): genuinely queryable (indexes, `ORDER BY`/
  sort, text search), a natural fit for "browse and search across owner" -
  but a new resource, new cost, and a new connection/ORM pattern this
  toy-not-a-system-of-record app has deliberately avoided so far (README
  section 4, section 9's five-resource footprint).
Whoever picks up this story should re-read `keepsake-gallery/04`'s Technical
Notes (the Table Storage precedent) and `infra/main.bicep`'s footprint before
choosing, and record the choice here and in `feature.md`'s Decisions log once
made - do not silently default to "just add another Table."

## Technical Notes
- Depends on `accounts-identity/02`'s `IAccountStore`/purchaser-identity seam
  to key the gallery (never invent a second purchaser-identity concept).
- Depends on `billing-entitlements/01`'s `EvaluateForSession` seam for
  AC-04 - add a capability key to the existing catalog
  (`api/src/Entitlements/`), do not build a parallel gate.
- Whatever datastore is chosen (see Datastore decision above), keep the
  service behind a small interface (mirroring `IPublishedTaleStore` from
  `keepsake-gallery/04` and `IAccountStore` from `accounts-identity/02`) so
  the API surface web consumes is stable regardless of the backing store.
- The synced record should reuse `keepsake-gallery/01`'s rendered image (or a
  server-renderable equivalent of it) plus the small metadata record from
  story 03 (title, date, byline names) - not a new content shape.
- Web-side: a purchaser-only "Cloud gallery" surface, reachable only once
  signed in (mirrors `accounts-identity/02`'s note that account-creation
  surfaces live inside the purchase flow, not a standalone entry point
  reachable from Home) - it must not appear or be hinted at for anonymous
  players, per AC-02.
- No change to `api/src/Rooms/`, `GameHub.cs`, or the round lifecycle - this
  is a purchaser-account-scoped read/write surface, entirely separate from
  session/round state, following the same isolation precedent
  `keepsake-gallery/04` set for its own server surface.

## Implementation record (2026-07-03)
Shipped in PR #157 (issue #154). All ACs met, reviewed clean (0 critical), CI green.
- **API (`api/src/CloudGallery/`):** an owner-scoped, bearer-credential-authenticated
  store + controller (`api/account/gallery` - GET list, POST save, DELETE one, DELETE
  revoke-all), isolated from `GameHub`/`Rooms`, auth mirrored from `EntitlementsController`.
  Owner key derived server-side (`AccountIdentity.KeyFor(account.Email)`), never from the
  request body (AC-05, IDOR-safe). Server-side re-vet of every part + byline on save
  (AC-05). Azure Table Storage (owner-partitioned) per the datastore decision, with a
  working in-memory fallback; revoke-all is fail-loud (AC-06). `gallery.cloudSync` added
  to the default-unlocked baseline, evaluated once on gallery-load (AC-04).
- **Web:** `cloudGalleryClient.ts`; `localGallery.ts` extended to persist flattened
  display parts + a `cloudTaleId` sync stamp (dedupe, AC-01); `CloudGallery.tsx` renders
  inside Account's signed-in state only (AC-02) with browse/search/sort (AC-03), consented
  upload, per-tale delete, and revoke-all (AC-06). The reveal flow stays credential-blind.
- **Enabler:** an app-wide in-memory `PurchaserSession` (accounts-identity/03, #69) so
  sign-in persists across navigation - without it, returning to the gallery forced a fresh
  sign-in every time. In-memory only (never persisted).
- **Verified:** the full signed-in flow end-to-end (sign-in -> list -> save -> re-vet
  reject -> delete -> revoke), sign-in persistence, empty state, and the AC-02 boundary.
- **Not migrated (as scoped):** existing device-local (story 03) tales saved before this
  work lack the text parts, so they are not uploadable - a documented, non-required gap.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: sign in as the same purchaser on two devices/browsers; confirm a tale saved on one appears on the other |
| AC-02 | manual: play and save tales with no sign-in; confirm no cloud gallery entry point appears and no cloud row is written |
| AC-03 | manual + unit (once built): search by title/byline returns the expected tale(s); a sort/filter (e.g. by date, or by like-count once available) reorders results correctly |
| AC-04 | code review: `gallery.cloudSync` capability key is read exactly once at session-creation via the existing entitlement seam, never per-save or per-view |
| AC-05 | manual: inspect a synced tale for any field beyond nickname + Guardian variant; confirm no unfiltered content path exists |
| AC-06 | manual: delete/revoke a purchaser's cloud sync; confirm associated tale rows are removed within the documented window |

## Dependencies
- accounts-identity/02-lightweight-purchaser-account (the purchaser account
  this gallery is keyed to - hard gate, must exist first)
- billing-entitlements/01-entitlement-model-and-session-creation-gate (the
  entitlement seam AC-04 consumes - hard gate, must exist first)
- keepsake-gallery/01-save-reveal-as-image (the rendered tale content this
  syncs)
- keepsake-gallery/03-tales-weve-carved-history (the local gallery this
  extends the concept of, and the fallback for every non-purchaser)
- infra (whatever datastore this story settles on - see Datastore decision)
