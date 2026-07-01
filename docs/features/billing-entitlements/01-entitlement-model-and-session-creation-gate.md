# Story: Entitlement model + session-creation gate

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** #70

## Context
This is THE seam (feature.md, README section 3, CLAUDE.md section 6): a
single service/hook that answers "is capability X unlocked for this session?"
- asked exactly once, at session-creation, and never again mid-session. Every
other story in this feature (tip jar, Stripe, gated purchase, restore) and
every future paid feature (add-on packs, AI illustration/voice/on-demand)
consumes this seam instead of inventing its own gate. Ships with everything
defaulted to unlocked, so introducing it changes zero observed behavior on
day one - gating a capability later is a config flip, not a refactor. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the capability-key catalog, when it is defined, then it
      contains at minimum: `library.full`, `play.remote`, `play.largeGroup`,
      `ai.illustration`, `ai.voice`, `ai.onDemand`, and an open-ended
      `pack.<id>` family for add-on packs - a single, typed enumeration/string
      catalog, not one-off booleans scattered per feature.
- [ ] AC-02: Given any capability key in the catalog and no purchaser signed
      in / no entitlement ever granted, when the check runs, then it returns
      "unlocked" - every capability defaults to free/unlocked so shipping this
      seam changes no current behavior (this is the core promise of the
      story).
- [ ] AC-03: Given a room/session is being created, when the entitlement check
      runs, then it runs exactly once, at that moment, and its result (which
      capabilities are unlocked for this session) is captured and reused for
      the session's lifetime - no capability check is ever re-evaluated
      per-request, per-round, or per-word-submission.
- [ ] AC-04: Given a future story or feature wants to gate a new capability,
      when it is added, then doing so requires only: (a) adding a key to the
      catalog and (b) reading the existing check at session-creation - no
      changes to the check's public shape, to `Room.cs`/`RoomRegistry.cs`
      internals, or to any hub method signature are required.
- [ ] AC-05: Given the entitlement check needs to know "is there an entitled
      purchaser behind this session," then it reads that from
      accounts-identity's seam (accounts-identity/01 AC-02,
      accounts-identity/02 AC-04) - it does not duplicate purchaser-lookup
      logic.
- [ ] AC-06: Given an entitlement is later granted to a purchaser (stories 03-04),
      when it is persisted, then it is stored in Azure Table Storage
      (README section 4), keyed so the session-creation check can resolve it
      by purchaser identity in a single lookup.
- [ ] AC-07: Given no purchaser account or entitlement exists anywhere yet
      (day one), when a room is created (single-player, or a 2-player group,
      Slice 1's existing flows), then session-creation succeeds exactly as it
      does today, with the check present but a no-op in effect - verified by
      re-running the existing session-engine/game-modes/group-play manual and
      automated coverage with zero regressions.

## Out of Scope
- Actually gating any capability (flipping a key from unlocked to
  entitlement-required) - this story ships the seam and the catalog with
  everything unlocked; a later, explicit decision (recorded in feature.md's
  Decisions log when it happens) turns on real gating for a specific key.
- The tip jar, Stripe integration, gated purchase UI, and restore/manage view
  - stories 02-05 consume this seam; this story only builds the seam and the
  storage it reads from.
- Per-word or per-round entitlement checks of any kind - explicitly
  disallowed by AC-03; if a future story proposes one, it is a design smell
  per feature.md.
- Rate limiting, quota metering (e.g. "N AI generations per month") - a
  session-creation gate answers unlocked/not-unlocked, not "how many are
  left"; metering is a later, separate concern if ever needed.

## Technical Notes
- New `api/src/Entitlements/` folder (mirrors the existing per-concern layout:
  `Rooms/`, `Safety/`, `Accounts/`). A `CapabilityKey` type (string-backed
  enum or well-known constants class - keep it simple, this is a toy, not a
  system of record per CLAUDE.md section 10) and an `IEntitlementService`
  with a single primary method shaped like
  `EvaluateForSession(purchaserIdentity?) -> SessionEntitlements` (a small,
  immutable set/record of which capability keys are unlocked). Register as a
  singleton or scoped service in `Program.cs`, following the existing
  `RoomRegistry` / `IContentSafetyFilter` DI pattern.
- **Call site is session-creation, full stop.** In practice this means: the
  hub method(s) that create a room (`api/src/Hubs/GameHub.cs`, session-engine/01)
  and the solo/single-player entry point (`web/src/pages/Solo.tsx` and its API
  equivalent, if any) are the only call sites. Do not add a call inside blank
  submission, round-collection, or reveal code paths (`game-modes/`,
  `group-play/`, `the-reveal`) - that would violate AC-03.
- Default-unlocked (AC-02) should be a literal default in code, not a
  configuration flag someone has to remember to set - e.g. the catalog itself
  ships with every key's default state as `Unlocked` and a grant record is
  what would ever *change* that per-purchaser, never the other way around.
- Storage: an `EntitlementGrant` row per purchaser + capability key (or a
  small set of keys) in Azure Table Storage, partitioned by purchaser
  identity for a single-lookup read at session-creation (AC-06). Reuse the
  same storage account/connection wiring `infra/main.bicep`'s `storage`
  resource already provisions - no new resource.
- Read side of accounts-identity: this story's check calls into
  `IAccountStore`/the purchaser-identity seam from accounts-identity/02, not
  a re-implementation (AC-05).
- Keep the public shape (`EvaluateForSession` and the `SessionEntitlements`
  result) intentionally minimal and stable - this is the contract every
  future paid-feature story imports (see the reuse map in
  implementation.md). Treat a breaking change to this method's signature as a
  cross-feature event, not a routine story edit.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/Entitlements/CapabilityKeyTests.cs (to be created with the API test project): the catalog contains the minimum keys.` |
| AC-02 | `api/tests/Entitlements/EntitlementServiceTests.cs: EvaluateForSession with no purchaser/no grants returns every key Unlocked.` |
| AC-03 | `manual + code read: grep for the check's call sites - confirm exactly the session-creation path(s), none in per-request code.` |
| AC-04 | `manual: add a throwaway test capability key at review time - confirm no Room.cs/hub-signature diff is needed to wire it.` |
| AC-05 | `api/tests/Entitlements/EntitlementServiceTests.cs: purchaser lookup delegates to IAccountStore (mock/fake), no duplicate lookup logic.` |
| AC-06 | `api/tests/Entitlements/EntitlementServiceTests.cs (integration-style, Table Storage emulator or fake): a granted entitlement round-trips.` |
| AC-07 | `tests/*.spec.ts (Playwright smoke) + existing session-engine/game-modes/group-play suites, re-run as regression: zero behavior change.` |

## Dependencies
- accounts-identity/01 (the "is there a signed-in purchaser" seam this story
  reads from) and accounts-identity/02 (the account record itself, for the
  purchaser-identity key used in AC-05/AC-06).
- session-engine (the session-creation call site this story hooks).
- infra (Table Storage already provisioned per README section 9).
