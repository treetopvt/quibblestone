# Story: Cloud-synced, browsable gallery for purchasers

**Feature:** Keepsake Gallery  ·  **Status:** Not Started  ·  **Issue:** TBD

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
- [ ] AC-01: Given a signed-in purchaser (accounts-identity/02) plays a round
      and saves a tale, when they sign in on a second device, then the same
      tale appears in their gallery there too - the gallery is keyed to the
      purchaser account, not to a device's local storage.
- [ ] AC-02: Given a player has never signed in as a purchaser (the common,
      anonymous case), when they save tales, then those tales stay in the
      device-local gallery from story 03 only - no cloud gallery is created,
      offered, or implied for them; anonymous players never get a cloud
      gallery, full stop (README section 6 - minimal data on minors; mirrors
      accounts-identity/01's "anonymous, forever" contract).
- [ ] AC-03: Given a purchaser's cloud gallery, then they can browse it beyond
      a simple recency list: search by title or byline name, and filter/sort
      (e.g. most-liked, once `the-reveal`'s reaction counts exist, or by
      date/session) - the access pattern this story exists to add, which
      `keepsake-gallery/03`'s local list and `04`'s single-slug lookup do not
      need to support.
- [ ] AC-04 (entitlement): Given a session is created, then whether the
      cloud-synced gallery is available for that session is decided once, at
      session-creation, by reading the `billing-entitlements/01` seam for a
      dedicated capability key (e.g. `gallery.cloudSync`) - not per-request,
      per-save, or per-gallery-view; the free tier keeps the device-local
      gallery (story 03) in full, and this story states plainly whether
      `gallery.cloudSync` ships default-unlocked (per
      `billing-entitlements/01` AC-02) or is a deliberately gated paid-tier
      perk - that decision is made when this story is picked up, not before.
- [ ] AC-05 (child-safety): Given any tale synced to or browsed in the cloud
      gallery, then it is the same already-filtered content story 01 already
      produced (no new free-text entry point), and the only identity attached
      to a synced tale is the in-session nickname(s) + Guardian variant(s) -
      never a real name, email, or other PII from the purchaser account leaks
      onto a tale a child might see.
- [ ] AC-06: Given a purchaser deletes their account or revokes cloud sync,
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

## Datastore decision (open - this story is the trigger to make it)
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
