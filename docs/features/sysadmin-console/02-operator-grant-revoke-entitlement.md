# Story: Operator grant / revoke an entitlement by purchaser email

**Feature:** Sys-Admin Console  ·  **Status:** Complete  ·  **Issue:** #136

## Context
The concrete need this story exists for (ADR 0002 "Recommendation" + Decision B): unstick a paying
customer whose entitlement did not apply, without hand-editing Azure Table Storage directly. An
operator looks a purchaser up by email and grants or revokes a capability; the write lands as the
same lease-shaped `EntitlementGrant` row `billing-entitlements` defines - `validThrough` + `source`
(ADR 0002 Decision C) - so the session-creation check (`billing-entitlements/01`) reads it exactly
like any other grant, with no special case. This story never touches player, room, or session data
- it operates entirely on the purchaser plane (email in, grant out), upholding the anonymity
invariant ADR 0002 defines for `CreateRoom`. It is the thinnest possible sliver: one or two
protected endpoints plus a minimal internal page, not a full admin app. See
[feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given a signed-in operator (story 01's boundary), when they enter a purchaser's email
      into the back office's lookup screen, then the purchaser's account (accounts-identity/02) and
      its current entitlement grants (capability key, `validThrough`, `source`) are displayed - or a
      clear "no account found for this email" state if none exists.
- [x] AC-02: Given a looked-up purchaser, when the operator grants a capability key from the
      `billing-entitlements/01` catalog, then an `EntitlementGrant` row is written keyed to that
      purchaser's identity with `source = operator` (or an equivalent explicit marker distinguishing
      it from a Stripe-driven grant) and a `validThrough` the operator sets (a specific date, or "no
      expiry" for a one-time-pack-shaped grant) - written through the exact same store
      `billing-entitlements/01`'s session-creation gate reads, not a parallel write path.
- [x] AC-03: Given a purchaser with an active grant, when the operator revokes a capability key,
      then the grant is removed or its `validThrough` is set to the past (whichever
      `billing-entitlements/01`'s store shape prefers) so the very next session-creation check for
      that purchaser evaluates that capability as no longer unlocked - consistent with "not
      per-request" (README section 3): an already-open session is unaffected, only new
      sessions see the change.
- [x] AC-04 (anonymity invariant, non-negotiable): Given any grant/revoke action, then it is keyed
      and displayed solely by purchaser identity (email) and capability keys - no player nickname,
      room code, session id, or gameplay data is ever looked up, joined, or displayed anywhere on
      this surface; the operator cannot navigate from a purchaser record to any room or player the
      purchaser's household played in.
- [x] AC-05 (admin-boundary reuse): Given these endpoints, then they are reachable only from the
      back office (story 01's separate bundle/route tree) and require the operator authorization
      policy from story 01 - never merely "signed in as a purchaser," and never exposed to the kid
      PWA or any player-facing route.
- [x] AC-06: Given an operator grants or revokes an entitlement, then the change is idempotent and
      low-ceremony - granting the same key twice does not create duplicate rows, and there is no
      audit-trail/approval-workflow ceremony beyond the operator seeing what they just did (this is
      operator convenience, not a compliance console, per feature.md's Design notes).

## Out of Scope
- Real charging / Stripe integration itself (`billing-entitlements/03-04`) - this story consumes
  the entitlement store those stories write to via webhook; it is the manual, human-operated side
  door, not a replacement for the automated grant path.
- Refunds, chargebacks, or subscription cancellation via Stripe - those stay on Stripe's own
  dashboard (feature.md's "what is NOT this feature" table); this story only edits the
  `EntitlementGrant` row, never talks to Stripe.
- Purchaser self-service restore/manage (`billing-entitlements/05`) - that is the purchaser's own
  read view of their entitlements; this story is the operator's write access on the purchaser's
  behalf.
- Bulk operations, CSV import, or granting to more than one purchaser at a time - one purchaser,
  looked up by one email, per action.
- Any player/room-facing change - AC-04 is a hard boundary, not a nice-to-have.
- Role-based access among multiple operators - Parked in feature.md; alpha has one operator.

## Technical Notes
- **Dependency reality: the entitlement *interface* is shipped, but the *grant store* this story
  writes to is not.** `IEntitlementService` + `SessionEntitlements` + the `EvaluateForSession`
  contract are already in `api/src/Entitlements/` (ai-cost-gate/02, #121, PR #132) and captured at
  `GameHub.CreateRoom` - but that is a thin, default-unlocked, read-only stand-in
  (`DefaultUnlockedEntitlementService`); it has no capability catalog beyond `ai.onDemand` and **no
  grant store to write to**. Grant/revoke needs `billing-entitlements/01` (#70) to add the
  lease-shaped `EntitlementGrant` store (`validThrough` + `source`) + the full catalog, and
  `accounts-identity/02` (#68) to add the by-email purchaser lookup - both still unbuilt (no
  `api/src/Accounts/`; catalog is `ai.*`-only). Mirror `ai-cost-gate/02`'s handling: either (a)
  serialize this story after #70 + #68 land, or (b) build against the exact `EntitlementGrant`
  (`validThrough` + `source`) and `IAccountStore` shapes those stories specify. Given this story's
  timing (pairs with real charging, `billing-entitlements/03-04`), prefer (a): by the time an operator
  needs to unstick a paying customer, #70 + #72 already exist.
- New `api/src/Admin/` additions (alongside story 01's operator-auth pieces): an
  `AdminEntitlementsController` (or minimal-API routes) with two actions - `GET
  /admin/purchasers/{email}` (lookup) and `POST /admin/purchasers/{email}/entitlements` (grant) /
  `DELETE /admin/purchasers/{email}/entitlements/{key}` (revoke) - each behind story 01's
  `[Authorize(Policy = "Operator")]`. These call `IAccountStore` (accounts-identity/02, lookup by
  email) and `IEntitlementService`'s grant-store write path (billing-entitlements/01) - they do not
  reimplement either.
- The grant write is the *same* `EntitlementGrant` shape `billing-entitlements/03`'s Stripe webhook
  writes (partition key = purchaser identity), with `source` distinguishing `stripe`/`subscription`
  from `operator` - purely descriptive, not a different code path or a different Table.
- Web: a single, minimal internal page (`web/admin/` per story 01's separate bundle) - an email
  search box, a small entitlements table with grant/revoke controls per capability key, reusing
  `web/src/theme.ts` tokens for visual consistency, not a bespoke admin design system. No FontAwesome
  icon set change beyond what story 01 already registers for the back office.
- Timing: this story is deliberately sequenced to land alongside real charging
  (`billing-entitlements/03-04`) per feature.md's Candidate stories table - building it earlier is
  not wrong, but it has nothing real to grant/revoke until a purchaser can actually buy something.
- *(2026-07-07: the "Dependency reality" bullet above is stale - #70 (billing-entitlements/01, PR
  #152: `IEntitlementGrantStore` + `StoredValueEntitlementService`) and #68 (accounts-identity/02,
  PR #147) have since shipped, so the built story writes through the real grant store and the real
  by-email account lookup, not a stand-in.)*

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs: a lookup by known email returns the account + grants; an unknown email returns a clear not-found state.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs: granting a capability key writes an EntitlementGrant with source=Operator and the given validThrough, readable by billing-entitlements/01's session-creation gate (EvaluateForSession).` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs: revoking a capability key (past-dating the lease) causes the next EvaluateForSession call for that purchaser to return it locked; a SessionEntitlements captured before the revoke is unaffected.` |
| AC-04 | `manual: code review + UI audit - confirm no player/room/session field is ever queried, joined, or rendered on the purchaser lookup or grant/revoke screen.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs: a purchaser-scoped credential (and no credential) is rejected by these endpoints; the page is unreachable from the kid PWA (separate admin bundle).` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Admin/AdminEntitlementsControllerTests.cs: granting the same key twice does not duplicate rows; no audit-log entity is required for the action to succeed.` |

## Dependencies
- `sysadmin-console/01` (this feature) - the operator login + admin boundary this story's endpoints
  sit behind.
- `billing-entitlements/01` (#70) - the `IEntitlementService` seam + lease-shaped `EntitlementGrant`
  store this story reads and writes against (or the thin contract-compatible stand-in, per
  Technical Notes, if #70 has not landed).
- `accounts-identity/02` (#68) - the purchaser account this story looks up by email.
- `billing-entitlements/03-04` - real charging; this story is most useful once these exist, though it
  is independently buildable and testable against #70's contract beforehand.
