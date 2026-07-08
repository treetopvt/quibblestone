# Story: Soft delete and restore

**Feature:** Keepsake Vault  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
Deletion in every existing keepsake surface is hard and immediate:
`TableStorageCloudGalleryStore.DeleteAsync`/`DeleteAllForOwnerAsync` remove a
row outright, and `TableStoragePublishedTaleStore.ConfirmHiddenAsync` (the
moderation takedown path, `sysadmin-console/03`) explicitly "hard-deletes the
tale body so the slug NEVER serves again." A mistaken delete, or a tale
wrongly flagged and confirmed-hidden by an operator, is today unrecoverable.
ADR 0003's Layer 2 names the fix: vault tale deletion and public-tale
takedowns become soft-deletes with a restore window, so "restore" becomes a
real operator support verb (`sysadmin-console/07` builds the console verb
itself; this story is the underlying data model and store behavior it will
call). See [feature.md](./feature.md) and
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) (Layer 2:
"soft-delete with a restore window (takedowns become soft-deletes too)").

## Acceptance Criteria
- [ ] AC-01: Given a player deletes a tale from their vault, then the tale is
      SOFT-deleted - marked deleted and timestamped rather than removed - and
      stops appearing in any listing (the vault's own `GET`, and the merged
      gallery view from `keepsake-vault/02`) immediately.
- [ ] AC-02: Given a soft-deleted vault tale within its restore window -
      **default 30 days** from the deletion timestamp, a settings-key
      candidate (`control-plane`'s "knob migration" list; ship as a code
      constant default until `control-plane/01`'s catalog exists, do not
      block on it) - then its data remains fully recoverable by a restore
      operation. This story ships the STORE method only (`RestoreAsync` or
      equivalent) - the operator console verb/UI that calls it is
      `sysadmin-console/07`, not this story.
- [ ] AC-03: Given a soft-deleted tale PAST its restore window, then it
      becomes eligible for real (hard) removal. Choose and RECORD one
      mechanism here once decided: lazy purge-on-read (mirroring
      `PublishedTale.IsExpired`'s existing "expired reads as gone, with a
      best-effort delete to reclaim the row" idiom - the same mechanism this
      feature's story 01 already uses for vault-TTL expiry) is the default,
      lower-ceremony choice consistent with this codebase's existing
      precedent; a background reaper job is noted as a Parked-Phase-2+
      alternative in `feature.md`, not built here.
- [ ] AC-04: Given a published tale takedown (the moderation `confirm-hidden`
      action, `sysadmin-console/03`, today implemented by
      `TableStoragePublishedTaleStore.ConfirmHiddenAsync`'s hard delete of the
      tale body), then it becomes a soft-delete instead, using the SAME
      restore-window model as AC-01/AC-02 - so a wrongly-hidden tale or an
      operator mistake is recoverable within the window. This story changes
      the STORE's takedown behavior only; the existing report -> auto-hide ->
      operator-review moderation flow itself (`TaleModeration`,
      `sysadmin-console/03`'s report/hide/restore actions) is otherwise
      untouched - `RestoreAsync` (the existing moderation restore, which today
      only un-hides a tale that was never body-deleted) and this story's new
      restore-from-soft-delete both exist side by side and must not be
      confused with each other in the store's naming.
- [ ] AC-05 (child-safety): Given a soft-deleted or restored tale, then no new
      free-text entry point or PII surface is introduced anywhere in this
      story's changes - restoring a tale returns EXACTLY the already-filtered
      content that existed before deletion, unmodified.
- [ ] AC-06: Given a restore (vault tale or published-tale takedown), then the
      tale reappears in its respective surface (the vault/gallery, or the
      public tale link) exactly as it was before deletion - no content
      mutation, no re-vetting, no re-publish ceremony.

## Out of Scope
- The sysadmin-console UI/verb that calls the restore method(s) this story
  builds - that is `sysadmin-console/07` ("Support lookup + verbs ... restore
  a soft-deleted tale"). This story ships the data model and store behavior
  only.
- `CloudGallery`'s delete semantics (`ICloudGalleryStore.DeleteAsync`/
  `DeleteAllForOwnerAsync`) - the story text this feature was scoped from
  names "vault tale deletion and public-tale takedowns" specifically, not the
  purchaser cloud gallery; `CloudGallery`'s hard-delete stays as-is. Note this
  explicitly as a deliberate scope line, since it is an easy story to conflate
  with vault deletion.
- Building a scheduled background reaper job for past-window soft-deletes -
  see AC-03's chosen mechanism (lazy purge-on-read); a job is a
  Parked-Phase-2+ alternative, not built here.
- Any change to the moderation REPORT/auto-hide THRESHOLD logic
  (`TaleModeration`'s `ReportCount`/`IsHidden` fields, or the per-IP report
  rate limit) - only the CONFIRM-HIDDEN action's underlying delete mechanism
  changes (hard delete -> soft delete), nothing about how a tale gets
  reported or auto-hidden in the first place.
- Un-deleting a tale whose restore window has already lapsed (and was
  purged per AC-03) - once purged, it is genuinely gone, consistent with
  "most data is mutable... nothing here is a system of record" (README
  section 4) applied to a bounded, not unbounded, grace period.

## Technical Notes
- **Vault side** (`api/src/Vault/`, extending story 01): add a `Deleted`
  marker to the tale's lifecycle without mutating the immutable content row
  in place - mirror the codebase's existing "rebuild the immutable record
  with a flipped flag" pattern (`Player.Connected`,
  `Room.MarkDisconnected`'s `_players[index] = _players[index] with
  { Connected = false }`) conceptually, or a `TaleModeration`-style tiny
  companion row (`DeletedUtc`, keyed by the same partition/row) if keeping
  the content row fully untouched is preferable - record which shape is
  chosen here once implemented. `IVaultStore.cs` gains `SoftDeleteAsync(vaultId,
  taleId)` and `RestoreAsync(vaultId, taleId)`; `ListAsync` excludes
  soft-deleted (and past-window-purged) tales by default (AC-01).
  `VaultController.cs` gains `DELETE /api/vault/{vaultId}/tales/{taleId}` (a
  player-facing soft-delete action) - `RestoreAsync` itself is NOT exposed
  via a public endpoint in this story (no console/auth surface exists yet
  for it; `sysadmin-console/07` adds that call site).
  - **File-footprint hazard**: `keepsake-vault/03` (claim + recovery) also
    touches `IVaultStore.cs`, `TableStorageVaultStore.cs`, and
    `VaultController.cs` in the SAME ADR 0003 wave (wave 3) - see this
    feature's `implementation.md` Wave Plan for the serialization call
    (03 owns the claim-related methods/endpoints; 04 owns the soft-delete-
    related methods/endpoints; land them as two small, sequential PRs rather
    than a parallel pair on the same files).
- **Published-tale side** (`api/src/PublishedTales/`): change
  `TableStoragePublishedTaleStore.ConfirmHiddenAsync` from calling
  `TryDeleteAsync` (hard delete) on the tale body to a soft-delete marker
  instead - the simplest shape consistent with the existing moderation
  companion-row precedent is a THIRD moderation state (alongside
  `ReportCount`/`IsHidden`) or a dedicated `DeletedUtc` column on the
  existing `ModerationPartitionKey` row, so a report/hide/confirm/restore
  cycle and a "confirm-hidden was itself wrong" restore can be told apart
  from each other. `IPublishedTaleStore.cs` gains a `RestoreFromTakedownAsync`
  (or similarly, clearly-named) method distinct from the EXISTING
  `RestoreAsync` (which today un-hides a tale that was reported but never
  body-deleted) - name them so an operator-console reader cannot confuse "un-
  hide" with "un-delete."
- **TTL/restore-window constant**: mirror
  `PublishedTalesController.TaleTtl`'s "a named constant, recorded in the
  story, promoted to a settings key later" pattern for the 30-day restore
  window - do not leave it a magic number.
- **Child safety (AC-05)**: restoring never re-runs `IContentSafetyFilter` -
  the content was already vetted at save/publish time and is returned
  byte-for-byte; a restore is a pure undo, not a fresh submission.

## Tests
| AC | Test |
|---|---|
| AC-01 | xUnit: soft-deleting a vault tale removes it from `ListAsync`'s result immediately; the underlying row still exists |
| AC-02 | xUnit: `RestoreAsync` (vault) returns a soft-deleted tale's original content unchanged, within the restore window |
| AC-03 | xUnit: a soft-deleted tale past the configured restore window reads as gone on the next list/fetch (lazy purge-on-read), mirroring `PublishedTale.IsExpired`'s existing test pattern |
| AC-04 | xUnit: `ConfirmHiddenAsync` no longer removes the tale row outright; a subsequent restore-from-takedown call returns the original tale content |
| AC-05 | code review: no new `IContentSafetyFilter.CheckAsync` call site is added by this story's restore paths |
| AC-06 | xUnit: a restored tale (vault or published) is byte-for-byte identical (title/parts/byline) to its pre-deletion content |

## Dependencies
- `keepsake-vault/01` (the vault store this story extends with soft-delete).
- Touches `api/src/PublishedTales/TableStoragePublishedTaleStore.cs` and
  `IPublishedTaleStore.cs` (the published-tale takedown path) - a
  shared-file hazard with any concurrent work in `PublishedTales/` from
  other features (e.g. `sysadmin-console/06`'s action log, if it happens to
  land in the same window); check the tree before starting.
