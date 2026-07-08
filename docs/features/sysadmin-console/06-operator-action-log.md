# Story: The operator action log (ADR 0003 Decision 3 / Amendment 2)

**Feature:** Sys-Admin Console  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
ADR 0002 originally staked out "no audit-trail ceremony" for the operator console (this feature's
own Design notes said the same: "resist growing it into role hierarchies, audit trails, or
dashboards"). ADR 0003 Decision 3 amends that stance, narrowly: **for the money/moderation plane
only**, a minimal append-only action log now exists, because the moment real money and takedowns
are involved, an operator's grant/revoke or takedown decision can be disputed, and today there is
no record of who did what, when. This is explicitly framed as **dispute insurance, not compliance
ceremony** (ADR 0003 Amendment 2): no immutability guarantee, no legal-hold, no retention policy
beyond a pragmatic cap. One row per action - operator email, action, target, timestamp, optional
note - written through a single shared seam every money/moderation-affecting endpoint calls, plus a
simple reverse-chronological view in the console's Operations job (story 05). Gameplay and content
stay exactly as ceremony-free as before; only this narrow slice of operator action gets a record.
See [feature.md](./feature.md) and [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)
Decision 3 and Amendment 2.

## Acceptance Criteria
- [ ] AC-01: Given an operator performs a money- or moderation-affecting action - grant an
      entitlement (story 02), revoke an entitlement (story 02), confirm a takedown (story 03),
      restore a takedown (story 03), or flip the Stripe mode (story 04) - when that action
      completes successfully, then exactly one append-only row is written recording the operator's
      email, the action name, the target (e.g. the purchaser email, the tale slug, or
      `"stripe-mode"`), a UTC timestamp, and an optional free-text note.
- [ ] AC-02 (one seam, not five): Given the five call sites in AC-01, then each writes through the
      SAME single `IOperatorActionLog` append method - no controller reimplements its own logging,
      and no second log store exists anywhere in the codebase.
- [ ] AC-03: Given a signed-in operator opens the Operations job's action-log view (story 05's
      Operations tab), when the view loads, then it lists the most recent entries in
      reverse-chronological order (newest first), capped at a sane page size (e.g. the latest 200),
      reachable only under the `ops` scope (story 05).
- [ ] AC-04 (dispute insurance, not compliance ceremony): Given the log's retention, then a
      pragmatic cap applies - e.g. keep the newest N thousand rows, or the newest M months,
      whichever the builder picks as a starting constant - with NO immutability guarantee (rows may
      be pruned past the cap) and no legal-hold mechanism; the constant is called out as a
      `control-plane/03` knob-migration candidate (a settings key) rather than hardcoded forever.
- [ ] AC-05: Given an action that fails before its effectful write (e.g. an invalid capability key
      on a grant attempt, or a not-found slug on a confirm/restore), then NO log row is written for
      it - only completed, effectful actions are logged, mirroring each controller's existing
      idempotent/no-op-on-not-found behavior.
- [ ] AC-06 (anonymity invariant applies to the log too): Given any log row, then it contains no
      identifier beyond the operator's own email and the target identifier already visible on that
      action's own surface (a purchaser email or a tale slug) - no player nickname, room code,
      session id, or any other PII ever appears in a log row, on the writer side or the view side.
- [ ] AC-07: Given `feature.md`'s Design notes ("Operator, not audit ceremony... Resist growing it
      into role hierarchies, audit trails, or dashboards"), when this story ships, then that note is
      AMENDED (not deleted) to cite ADR 0003 Decision 3 / Amendment 2 as the narrow, deliberate
      exception for the money/moderation plane - gameplay and content stay ceremony-free; only
      operator actions on money/moderation now carry one log row each.

## Out of Scope
- Any log write for gameplay, content, or player actions - the log covers ONLY operator actions on
  the money/moderation plane (AC-01's five actions today, plus story 07's support verbs once they
  land). Nothing about a room, a player's word, or a reveal is ever logged.
- Immutability, tamper-evidence, cryptographic chaining, or any compliance-grade audit mechanism -
  explicitly rejected by ADR 0003 Amendment 2 ("no immutability guarantees").
- A configurable retention policy UI - the retention cap is a constant (AC-04) this story picks; a
  settings-key-driven retention cap is `control-plane/03`'s knob-migration job, not this story's.
- Filtering, search, or export on the action-log view - a plain reverse-chronological list is the
  whole brief; a search-by-target convenience can be a later, separate addition if it earns its
  keep.
- Logging story 07's support verbs (resend link, extend TTL, restore, comp, resync) - those calls
  are added when story 07 builds those verbs, reusing this story's `IOperatorActionLog` seam; this
  story's own footprint only wires the FIVE actions named in AC-01.

## Technical Notes
- **New store, mirrors existing patterns:** `api/src/Admin/IOperatorActionLog.cs` (the append
  contract: `AppendAsync(operatorEmail, action, target, note, cancellationToken)`) +
  `TableStorageOperatorActionLog.cs`, following the same DI/registration shape as
  `TableStorageActiveStripeModeStore.cs` / `IEntitlementGrantStore`'s Table Storage implementations.
  For reverse-chronological listing without a client-side sort over a large partition, use the
  standard Azure Table "inverted ticks" RowKey trick (e.g. `RowKey =
  (DateTimeOffset.MaxValue.Ticks - timestamp.Ticks).ToString("d19")` prefixed or combined with a
  disambiguator) so a plain ascending RowKey query already returns newest-first - do not add a
  full-partition scan-and-sort in the controller.
- **Five call sites, one seam (AC-02):** inject `IOperatorActionLog` into
  `AdminEntitlementsController` (grant, revoke), `ReportedTalesController` (confirm, restore), and
  `StripeModeController` (the Set action, story 04) - each existing action calls `AppendAsync` ONCE,
  immediately after its own successful write, with a small, action-specific target string (e.g. the
  purchaser email, the tale slug, or the literal `"stripe-mode"`). Guard against a not-found /
  no-op path already returning early WITHOUT a call to `AppendAsync` (AC-05) - re-use each
  controller's existing early-return branches, do not add a new failure-classification layer.
- **New view surface:** `GET /api/admin/action-log` behind the `ops` scope (story 05's mechanism),
  paginated or capped server-side (AC-03), plus a small `web/src/admin/ActionLogView.tsx` +
  `actionLogClient.ts` placed in story 05's Operations tab (`OperationsPanel.tsx`).
- **The split (per the cross-feature build order):** the WRITE seam (`IOperatorActionLog` + the
  five `AppendAsync` calls) has no technical dependency on story 05's shell reorganization - it
  could be built and merged in parallel with 05. It is sequenced to land after 05 in this feature's
  Wave Plan only because the reverse-chronological VIEW needs an Operations tab to live in
  (story 05 creates that tab). A builder free to split further may land the write seam earlier;
  `implementation.md` records this split explicitly so the orchestrator's Phase 1 can decide.
- **Retention constant (AC-04):** a `public const int` or `TimeSpan` alongside the store (e.g.
  `MaxRetainedRows` or `MaxRetainedAge`), with a comment naming it a `control-plane/03` knob-
  migration candidate - the same pattern `ReportedTalesController`'s `AutoHideThreshold` already
  established for "pick a small starting value, leave a tuning comment."
- **`feature.md` amendment (AC-07):** edit this feature's own `feature.md` Design notes bullet
  ("Operator, not audit ceremony...") to ADD a sentence citing ADR 0003 Decision 3 / Amendment 2 as
  the narrow exception - do not delete the existing sentence; the "toy, not a system of record"
  posture for gameplay is unchanged and worth restating alongside the amendment.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: each of grant/revoke/confirm/restore/stripe-mode-flip writes exactly one row with the expected operator/action/target/timestamp.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: a shared fake IOperatorActionLog captures calls from all three controllers, proving one seam.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Admin/ActionLogControllerTests.cs: seeded rows return newest-first, capped at the page size.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: writes beyond the retention cap evict the oldest rows (or age out), with no exception.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs, ReportedTalesControllerTests.cs (extended): a not-found / no-op branch produces zero log rows.` |
| AC-06 | `manual: code review of TableStorageOperatorActionLog.cs and ActionLogView.tsx - confirm no field beyond operator email + target identifier is ever stored or rendered.` |
| AC-07 | `manual: diff review - feature.md's "Operator, not audit ceremony" note is amended (not removed), citing ADR 0003.` |

## Dependencies
- `sysadmin-console/05` (#TBD) - the Operations tab this story's view lives in (the VIEW depends on
  05; the write seam itself does not - see Technical Notes' "the split").
- `sysadmin-console/02` (#136, Complete), `03` (#137, Complete), `04` (#TBD) - the existing
  grant/revoke, confirm/restore, and Stripe-mode-flip call sites this story adds one logging call
  each to.
