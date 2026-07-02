<!--
  Story 02 of the AI cost gate - the entitlement seam consumed at session-creation. No em dashes.
-->

# Story: Entitlement check at session-creation (reserve `ai.*`, default-unlocked)

**Feature:** AI Cost Gate  ·  **Status:** Not Started  ·  **Issue:** #121

## Context
The gate's second piece (feature.md; ROADMAP "The AI cost gate" piece 2): one
entitlement check, evaluated exactly once when a room/solo session is minted, that
decides which AI capabilities this session may use. It consumes the
`billing-entitlements/01` seam (issue #70) - it does NOT invent a parallel gate.
Per [ADR 0001](../../adr/0001-ai-provider.md) decision C, in alpha the AI jumble is
free for everyone, so this check evaluates the jumble's `ai.*` key as unlocked and
does not block it; the seam is still built and the key reserved so real charging
attaches to the same place later without a refactor. The point of building it now:
retrofitting an entitlement dimension onto an anonymous, per-session AI flow later
is painful (CLAUDE.md section 6). See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a room/solo session is created, when the session-creation code
      runs, then the AI entitlement for that session is evaluated EXACTLY ONCE, at
      that moment, via the `billing-entitlements/01` seam
      (`IEntitlementService.EvaluateForSession`), and the result is captured on the
      session for its lifetime - never re-evaluated per-tap, per-round, or per-AI-call
      (that would be the smell `billing-entitlements` forbids).
- [ ] AC-02: Given the capability catalog, then it reserves the AI word-bank key the
      jumble uses (`ai.onDemand`, or a dedicated `ai.wordBank`, matching the catalog
      `billing-entitlements/01` AC-01 already defines) - this story does not create a
      new catalog, it consumes/extends the existing one.
- [ ] AC-03 (default-unlocked): Given no purchaser and no grant (every session in
      alpha), when the check runs, then the AI capability returns UNLOCKED - shipping
      this changes zero observed behavior, exactly as `billing-entitlements/01` AC-02
      promises. The jumble is reachable by all sessions.
- [ ] AC-04 (alpha gating is quota + breaker, not entitlement): Given the alpha
      decision (ADR 0001 C), then the jumble's actual gating is the rate-limit/quota
      (story 03) and the spend circuit-breaker (story 04), NOT this entitlement
      check - which is present, wired, and unlocked. The seam exists so that turning
      on real gating later is a stored-value flip (a grant becomes required), not new
      gating code.
- [ ] AC-05 (anonymous, per session): Given the check, then it keys off the session
      (the anonymous room/solo session), never a player identity or PII - consistent
      with "meter compute per session, not identity" (README section 6). If there is
      no purchaser (the norm), the check simply returns the default-unlocked set.
- [ ] AC-06 (single call site): Given the gate, then the ONLY call sites are
      session-creation: the room-create hub method (`GameHub.CreateRoom`,
      session-engine) and the solo entry point. No AI-entitlement check appears in
      any per-call, per-tap, per-round, or reveal code path (AC-01 restated as a
      code-location guard).
- [ ] AC-07 (no regression): Given day-one flows (solo, 2-player group), when a
      session is created, then it succeeds exactly as today with the check present
      but unlocked - re-run existing session-engine/game-modes/group-play coverage
      with zero regressions.

## Out of Scope
- Building the entitlement seam itself - that is `billing-entitlements/01` (#70);
  this story CONSUMES it. If #70 has not shipped when this is built, see Technical
  Notes for the thin, contract-compatible fallback (still default-unlocked).
- Stripe, purchase flow, real charging (`billing-entitlements` 02-05) - explicitly
  deferred (feature.md Parked).
- Turning any AI key from unlocked to entitlement-required - a later, explicit
  product decision, not this story (which ships everything unlocked).
- Quota metering / "N calls left" (story 03) and the spend breaker (story 04) - the
  entitlement gate answers unlocked/not, never how-many-left.

## Technical Notes
- **Consume, do not duplicate.** Call `IEntitlementService.EvaluateForSession(...)`
  from `billing-entitlements/01` at the session-creation call site and stash the
  resulting `SessionEntitlements` on the session (e.g. on `Room`/the solo session
  context) so later code reads the captured result, never re-evaluates.
- **Dependency reality (#70 not yet built).** `billing-entitlements/01` is Not
  Started; so is its `accounts-identity` chain. Two clean options, decided at
  orchestration time and noted in implementation.md: (a) serialize this story after
  #70 lands; or (b) build a minimal `IEntitlementService` shaped exactly to #70's
  contract (`EvaluateForSession(purchaserIdentity?) -> SessionEntitlements`,
  everything default-unlocked, no Stripe/accounts), which #70 later subsumes. Either
  way the PUBLIC shape is #70's, so consumers never change. Prefer (a) if #70 is
  close; (b) keeps this slice unblocked without pulling the whole billing chain in.
- **Call site** is `GameHub.CreateRoom` (session-engine) and the solo entry point -
  the same locations `billing-entitlements/01` names. Thread the captured
  entitlements to where the jumble path reads them (the proxy call site), without
  adding a per-call check.
- No new web surface. No secrets. Async; nullable respected.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/Ai/...` + code read: the AI entitlement is evaluated once at session-creation and captured; grep confirms no per-call re-eval |
| AC-02 | code review: the `ai.*` key the jumble uses exists in the catalog (reserved, not newly invented) |
| AC-03 | `api/tests`: `EvaluateForSession` with no purchaser returns the AI key Unlocked |
| AC-04 | code review: the jumble's runtime gate is story 03/04, not this check; the check is unlocked and non-blocking |
| AC-05 | code review: the check keys off the anonymous session, no PII/identity when no purchaser |
| AC-06 | code review: only session-creation call sites; none in per-call/tap/round/reveal paths |
| AC-07 | existing session-engine/game-modes/group-play suites re-run as regression: zero behavior change |

## Dependencies
- `billing-entitlements/01` (#70) - the `IEntitlementService` seam + `ai.*` catalog
  keys this consumes (or the thin contract-compatible fallback per Technical Notes).
- `session-engine` - the room/solo session-creation call site.
- cost-gate/01 (the proxy the captured entitlement result ultimately guards, once
  real gating is ever turned on).
