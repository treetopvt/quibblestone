# Story: The operator action log (ADR 0003 Decision 3 / Amendment 2)

**Feature:** Sys-Admin Console  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
ADR 0002 originally staked out "no audit-trail ceremony" for the operator console (this feature's
own Design notes said the same: "resist growing it into role hierarchies, audit trails, or
dashboards"). ADR 0003 Decision 3 amends that stance, narrowly: **for the money/moderation plane
only**, a minimal append-only action log now exists, because the moment real money and takedowns
are involved, an operator's grant/revoke or takedown decision can be disputed, and today there is
no record of who did what, when. This is explicitly framed as **dispute insurance, not compliance
ceremony** (ADR 0003 Amendment 2): no immutability guarantee, no legal-hold, no unbounded retention -
but retention must still be trustworthy (see below). One row per action - operator email, action,
target, timestamp, optional note - written through a single shared seam every money/moderation-
affecting endpoint calls, plus a simple reverse-chronological view in the console's Operations job
(story 05). Gameplay and content stay exactly as ceremony-free as before; only this narrow slice of
operator action gets a record.

**Revised 2026-07-08 (adversarial review).** The review found two ways this "dispute insurance"
could quietly fail the exact dispute it exists for:
1. **Ordering.** Writing the log row "immediately after" the effectful write means an action can
   commit successfully while its log append fails (a transient Table Storage error, a crash between
   the two calls) - leaving the disputed action with NO trail at all. The fix: the log row is
   written BEFORE the effectful action proceeds (see AC-01a and Technical Notes' "log-before-act").
2. **Retention.** A prunable, operator-lowerable retention cap lets the very party a dispute is
   about (an operator whose action is in question) volume-evict old rows by generating log noise, or
   config-evict them by lowering the cap, either of which erases the incriminating row before a
   dispute surfaces. The fix: retention is AGE-based with a HARD FLOOR no runtime setting can lower
   below (AC-04).

This story is also confirmed as the ONE shared seam for a wider action set than originally scoped:
`control-plane/01` now requires every settings change to append a row too (ADR 0003's "Security
posture" section, "the control plane cannot disable its own safety rails"), reusing this story's
`IOperatorActionLog` exactly as story 07's support verbs will (AC-01/AC-01a). See
[feature.md](./feature.md) and [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)
Decision 3, Amendment 2, and its "Security posture" section ("the action log is trustworthy dispute
insurance").

## Acceptance Criteria
- [ ] AC-01 (the action set - REVISED 2026-07-08, now six named actions today): Given an operator
      performs a money-, moderation-, or settings-affecting action - grant an entitlement (story
      02), revoke an entitlement (story 02), confirm a takedown (story 03), restore a takedown
      (story 03), flip the Stripe mode (story 04), or change a runtime settings override
      (`control-plane/01`, once it lands and calls this story's seam itself) - when that action is
      attempted, then exactly one append-only row is written recording the operator's email, the
      action name, the target (e.g. the purchaser email, the tale slug, the settings key, or
      `"stripe-mode"`), a UTC timestamp, and an optional free-text note. `IOperatorActionLog`'s
      action name is a free-form string, not a closed enum this story would need to extend - so
      `control-plane/01` (and later story 07) can start appending their own rows the moment they
      land, with zero code change here.
- [ ] AC-01a (log-before-act ordering - NEW, 2026-07-08 adversarial review, replaces the prior
      "immediately after its own successful write" ordering): Given any of the call sites in AC-01,
      when the action's INPUT VALIDATION has already passed (so AC-05's "no row for a
      never-attempted action" still holds), then the log row is appended BEFORE the effectful write
      is attempted - and if the append itself fails (the log store is unavailable), the request
      ABORTS before the effectful write runs at all, returning an error to the operator rather than
      silently proceeding. This inverts the prior failure mode (an action could commit with no
      trail) into a strictly safer one (in the rare case the effectful write then fails after a
      successful append, a log row exists recording an ATTEMPTED action with no corresponding
      effect) - an over-inclusive trail is the acceptable failure mode for dispute insurance; an
      under-inclusive one is not. An equivalent, stronger alternative a builder may choose instead -
      an outbox pattern making the log write and the effectful write part of one atomic unit - also
      satisfies this AC; "write the log after and hope" does not.
- [ ] AC-02 (one seam, not several): Given the call sites named in AC-01, then each writes through
      the SAME single `IOperatorActionLog` append method - no controller reimplements its own
      logging, and no second log store exists anywhere in the codebase.
- [ ] AC-03: Given a signed-in operator opens the Operations job's action-log view (story 05's
      Operations tab), when the view loads, then it lists the most recent entries in
      reverse-chronological order (newest first), capped at a sane page size (e.g. the latest 200),
      reachable only under the `ops` scope (story 05).
- [ ] AC-04 (age-based retention with a hard floor - REVISED 2026-07-08, replaces the prior
      "pragmatic cap... no immutability guarantee" wording that left the cap operator-lowerable):
      Given the log's retention, then it is AGE-based (a `MaxRetainedAge`, e.g. rows older than N
      months are eventually pruned) rather than a bare row-count cap, and a HARD FLOOR (a compiled
      constant, e.g. a minimum of 180 days) is enforced in code such that no runtime setting - not
      even a future `control-plane/03` knob-migration of this value - can configure retention BELOW
      that floor; a knob may only ever RAISE retention above the floor, never lower it below. This
      closes the review finding that a prunable, operator-lowerable cap lets the party a dispute is
      about volume-evict (generate enough log noise to push old rows out) or config-evict
      (lower the cap) the very rows a dispute would need. There is still NO immutability guarantee
      and no legal-hold mechanism above the floor - this remains dispute insurance, not a compliance
      archive.
- [ ] AC-05: Given an action that fails before its effectful write is even attempted (e.g. an
      invalid capability key on a grant attempt, or a not-found slug on a confirm/restore), then NO
      log row is written for it - only actions that pass validation and reach the log-before-act
      step (AC-01a) are logged, mirroring each controller's existing idempotent/no-op-on-not-found
      behavior.
- [ ] AC-06 (anonymity invariant applies to the log too): Given any log row, then it contains no
      identifier beyond the operator's own email and the target identifier already visible on that
      action's own surface (a purchaser email, a tale slug, or a settings key) - no player nickname,
      room code, session id, or any other PII ever appears in a log row, on the writer side or the
      view side.
- [ ] AC-07 (view-side encoding and input validation - NEW, 2026-07-08 adversarial review): Given
      `ActionLogView.tsx` renders a row's operator-influenced fields (the target string and the
      optional free-text note), then it NEVER uses `dangerouslySetInnerHTML` or any equivalent raw-
      HTML injection - it relies on React's default text-escaping for both fields, same as every
      other admin-bundle screen - and the API validates that any target claiming to be an email
      address matches a standard email-format check before the row is written (rejecting malformed
      or markup-bearing strings at write time, not merely at render time).
- [ ] AC-08: Given `feature.md`'s Design notes ("Operator, not audit ceremony... Resist growing it
      into role hierarchies, audit trails, or dashboards"), when this story ships, then that note is
      AMENDED (not deleted) to cite ADR 0003 Decision 3 / Amendment 2 as the narrow, deliberate
      exception for the money/moderation/settings plane - gameplay and content stay ceremony-free;
      only operator actions on money/moderation/settings now carry one log row each.

## Out of Scope
- Any log write for gameplay, content, or player actions - the log covers ONLY operator actions on
  the money/moderation/settings plane (AC-01's six actions today, plus story 07's support verbs and
  any future `control-plane` settings actions once they land, all reusing this same seam). Nothing
  about a room, a player's word, or a reveal is ever logged.
- Immutability, tamper-evidence, cryptographic chaining, or any compliance-grade audit mechanism -
  explicitly rejected by ADR 0003 Amendment 2 ("no immutability guarantees"). The age-based floor in
  AC-04 is a MINIMUM RETENTION guarantee, not an immutability or tamper-evidence guarantee - a row
  cannot be deleted early by config or volume, but this story adds no signing, hashing, or
  append-only storage enforcement beyond ordinary Table Storage writes.
- A configurable retention CEILING or a UI for tuning retention - the floor is a compiled constant
  this story ships; letting a LATER value raise retention above the floor is `control-plane/03`'s
  knob-migration job, not this story's, and no such knob may ever be configured below the floor
  regardless of which story builds it.
- Filtering, search, or export on the action-log view - a plain reverse-chronological list is the
  whole brief; a search-by-target convenience can be a later, separate addition if it earns its
  keep.
- Building `control-plane/01`'s own settings-change call site into this story's own footprint - this
  story ships the seam (AC-01's free-form action name accepts a settings-change row from day one);
  the call site living inside `control-plane/01`'s own settings controller is that other feature's
  job to add when it builds.
- Logging story 07's support verbs (resend link, extend TTL, restore, comp, resync) - those calls
  are added when story 07 builds those verbs, reusing this story's `IOperatorActionLog` seam; this
  story's own footprint only wires the SIX call sites it directly touches (AC-01).

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
- **Log-before-act ordering (AC-01a), precisely:** each call site's existing shape - validate input
  (early-return on invalid/not-found, unchanged, AC-05) -> perform the effectful write - now becomes:
  validate input (unchanged) -> `await _actionLog.AppendAsync(...)` -> IF that append throws, let the
  exception propagate (the request fails BEFORE the effectful write runs, e.g. before
  `IEntitlementGrantStore.SaveAsync` or `IActiveStripeModeStore.SetAsync` is called) -> otherwise
  proceed to the effectful write. This is a small reordering of each of the five existing call sites
  (grant, revoke, confirm, restore, mode-flip) plus the new settings-change call site elsewhere - not
  a redesign of any of them. A builder who prefers a stronger guarantee may instead implement an
  outbox (write both the log row and an "intent" record in one Table Storage batch transaction where
  both entities share a partition key, then perform the effect and mark the intent complete) - either
  satisfies AC-01a; do not ship a design where the log write happens strictly after the effect with
  no ordering guarantee at all.
- **Six call sites (today), one seam (AC-01/AC-02):** inject `IOperatorActionLog` into
  `AdminEntitlementsController` (grant, revoke), `ReportedTalesController` (confirm, restore), and
  `StripeModeController` (the Set action, story 04) - each existing action calls `AppendAsync` ONCE,
  per the log-before-act ordering above, with a small, action-specific target string (e.g. the
  purchaser email, the tale slug, or the literal `"stripe-mode"`). `control-plane/01`'s settings
  controller becomes a seventh caller once it lands, appending with the settings key as the target -
  this story does not build that call site, only the seam it calls into (see Out of Scope). Guard
  against a not-found / no-op path already returning early WITHOUT a call to `AppendAsync` (AC-05) -
  re-use each controller's existing early-return branches, do not add a new failure-classification
  layer.
- **New view surface:** `GET /api/admin/action-log` behind the `ops` scope (story 05's mechanism),
  paginated or capped server-side (AC-03), plus a small `web/src/admin/ActionLogView.tsx` +
  `actionLogClient.ts` placed in story 05's Operations tab (`OperationsPanel.tsx`).
- **View-side safety (AC-07):** `ActionLogView.tsx` renders the target and note fields as plain React
  children/text props (`<Typography>{row.target}</Typography>`, never `<div
  dangerouslySetInnerHTML={{ __html: row.note }} />` or string-concatenated markup) - React's default
  escaping is sufficient and this story adds no new escaping logic, it only forbids bypassing it. On
  the write side, the controller/seam validates any target string that is shaped like an email
  address (a standard `System.Net.Mail.MailAddress`-style or regex check) before persisting the row -
  rejecting the write (400) rather than silently storing an unparseable or markup-bearing string;
  non-email targets (a tale slug, a settings key, the literal `"stripe-mode"`) are not subject to the
  email-format check, only to normal length/charset sanity.
- **The split (per the cross-feature build order):** the WRITE seam (`IOperatorActionLog` + the
  six `AppendAsync` call sites this story itself wires) has no technical dependency on story 05's
  shell reorganization - it could be built and merged in parallel with 05. It is sequenced to land
  after 05 in this feature's Wave Plan only because the reverse-chronological VIEW needs an
  Operations tab to live in (story 05 creates that tab). A builder free to split further may land the
  write seam earlier; `implementation.md` records this split explicitly so the orchestrator's Phase 1
  can decide.
- **Retention constant (AC-04):** a `public const int MinRetentionDays` (e.g. `180`) alongside the
  store, enforced as a hard floor in the pruning/eviction code path - any retention value read from a
  future settings override (`control-plane/03`) is clamped UP to this floor before use, never honored
  below it; the floor itself is a compiled constant, never itself a runtime setting. Comment it as a
  `control-plane/03` knob-migration candidate for RAISING retention only.
- **`feature.md` amendment (AC-08):** edit this feature's own `feature.md` Design notes bullet
  ("Operator, not audit ceremony...") to ADD a sentence citing ADR 0003 Decision 3 / Amendment 2 as
  the narrow exception, now covering money/moderation/settings actions - do not delete the existing
  sentence; the "toy, not a system of record" posture for gameplay is unchanged and worth restating
  alongside the amendment.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: each of grant/revoke/confirm/restore/stripe-mode-flip writes exactly one row with the expected operator/action/target/timestamp; the action-name parameter accepts an arbitrary string (proving no closed enum blocks a future settings-change caller).` |
| AC-01a | `tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: a fake IOperatorActionLog that throws on AppendAsync causes the controller action to return an error WITHOUT the effectful write (e.g. IEntitlementGrantStore.SaveAsync) ever being invoked - proving log-before-act ordering, not log-after-effect.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: a shared fake IOperatorActionLog captures calls from all three controllers, proving one seam.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Admin/ActionLogControllerTests.cs: seeded rows return newest-first, capped at the page size.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: a simulated retention-override value below MinRetentionDays is clamped to the floor before being applied; rows older than the floor may be pruned, rows within the floor are never pruned regardless of volume or a lower configured value.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs, ReportedTalesControllerTests.cs (extended): a not-found / no-op branch produces zero log rows.` |
| AC-06 | `manual: code review of TableStorageOperatorActionLog.cs and ActionLogView.tsx - confirm no field beyond operator email + target identifier is ever stored or rendered.` |
| AC-07 | `web/src/admin/ActionLogView.test.tsx: a row with an HTML-bearing note renders as literal text, not markup; static grep confirms no dangerouslySetInnerHTML in the admin bundle. tests/QuibbleStone.Api.Tests/Admin/OperatorActionLogTests.cs: a malformed "email-shaped" target is rejected (400) at write time.` |
| AC-08 | `manual: diff review - feature.md's "Operator, not audit ceremony" note is amended (not removed), citing ADR 0003 and the money/moderation/settings scope.` |

## Dependencies
- `sysadmin-console/05` (#TBD) - the Operations tab this story's view lives in (the VIEW depends on
  05; the write seam itself does not - see Technical Notes' "the split").
- `sysadmin-console/02` (#136, Complete), `03` (#137, Complete), `04` (#TBD) - the existing
  grant/revoke, confirm/restore, and Stripe-mode-flip call sites this story adds one logging call
  each to.
- `control-plane/01` (ADR 0003 Layer 1, new feature) - not a build dependency (this story does not
  wait on it), but its settings controller becomes a consumer of this story's `IOperatorActionLog`
  seam once it lands; no code change here is needed when it does (AC-01's free-form action name).
