# Story: Anonymous player, forever

**Feature:** Accounts & Identity  ·  **Status:** Complete  ·  **Issue:** #67

## Context
Slice 1 already ships a working anonymous join flow (session-engine/02:
nickname + Guardian variant, no account, no PII). Before any purchaser account
exists, that flow needs to be formalized as a **contract** the rest of the app
can assume: a player is, and forever remains, "no account" unless and until a
separate purchaser account exists in the same session (accounts-identity/02).
This story does not change the join UI - it defines the seam so account
stories 02-03 and every entitlement check in billing-entitlements bolt on
without disturbing free play. See [feature.md](./feature.md) and README
section 3 ("Players are anonymous forever").

**Shipped-reality update (2026-07-03):** the "signed-in purchaser" seam this
story used to need to NAME is no longer hypothetical. `ai-cost-gate/02` (#121,
PR #132) already ships it as `Room.Entitlements` (a captured
`SessionEntitlements` capability set, never a purchaser id) via
`Room.CaptureEntitlements`, called exactly once from `GameHub.CreateRoom`. This
story's remaining job is verifying the shipped shape upholds the contract
below - AC-01 through AC-04 read as ALREADY TRUE in the deployed code - rather
than inventing a new placeholder field.

## Acceptance Criteria
- [x] AC-01: Given a player joins a room with a code and nickname (the
      existing session-engine flow), when the join completes, then the room's
      player record contains only the nickname and chosen Guardian variant -
      no email, no device identifier tied to a person, no account reference of
      any kind.
- [x] AC-02: Given the server-side room/player model, when it is inspected,
      then the only session-level entitlement seam is `Room.Entitlements` (a
      `SessionEntitlements` capability-key set, captured exactly once via
      `Room.CaptureEntitlements` - ai-cost-gate/02, #121, PR #132) - it carries
      capabilities only, NEVER a purchaser identity, and there is no
      per-player account field, login prompt, or optional-sign-in affordance
      anywhere in the join or lobby flow. (Already true in shipped code; this
      AC is a verification, not new work.)
- [x] AC-03: Given a room with no signed-in purchaser at all (the common
      case), when the room plays a full round end to end (join, lobby, word
      entry, reveal), then nothing in that flow requires, prompts for, or
      references an account - free play is provably login-free.
- [x] AC-04: Given the nickname is free text, when it is submitted, then it
      still passes the existing safety filter (child-safety/01) before it is
      stored or shown to anyone - this story does not weaken or bypass that
      check.
- [x] AC-05: Given this story lands, when accounts-identity/02 and 03 are
      built later, then they add a *new*, separate purchaser-account record
      (keyed independently, per feature.md) rather than modifying `Room.cs` /
      `RoomRegistry.cs` or `Room.Entitlements`'s capability-only shape -
      confirmed by a code-level check. `Room.CaptureEntitlements` already
      ships (ai-cost-gate/02) and stores only the resolved capability set;
      accounts-identity/02's account record must never be referenced from
      `Room.cs`.

## Out of Scope
- Any UI change to Join, Lobby, or the join flow - this is a documentation-
  and-hardening pass over an existing, working contract, not a new screen.
- The purchaser account itself (accounts-identity/02).
- Remembering a player's nickname/Guardian across devices or sessions -
  players remain anonymous and stateless between rooms (README section 3;
  also called out as parked in session-engine/feature.md).
- Any change to the safety filter itself (child-safety owns that).

## Technical Notes
- This is primarily a **contract-and-verification** story: the existing
  `api/src/Rooms/Room.cs` and `api/src/Rooms/RoomRegistry.cs` (session-engine/01)
  already store only nickname + Guardian variant per player, and
  `Room.Entitlements`/`Room.CaptureEntitlements` (ai-cost-gate/02, #121, PR #132)
  already ships the one session-level entitlement seam, storing ONLY a
  capability-key set - never a purchaser id. Extend the header-comment block
  on `Room.cs` to explicitly document both facts: "this record is PII-free by
  design; do not add an account/device/email field here - a purchaser
  account, if any, lives in a separate record (accounts-identity); the one
  entitlement seam is `Room.Entitlements`, capability keys only." This matches
  the verbose-header-comment convention already in the tree (see
  `RoomRegistry.cs`).
- There is no placeholder seam left to NAME - `Room.Entitlements` already IS
  that seam, shipped. This story's remaining job is pointing the header
  comment at it explicitly (so a future reader does not go looking for a
  separate boolean flag) and verifying by code read that it never carries a
  purchaser identity, upholding ADR 0002's load-bearing invariant.
- No API endpoint, no hub method, and no web component changes are expected
  for AC-01 through AC-04 - they are true today. Verification is a targeted
  code read (including `GameHubEntitlementTests.cs`'s existing coverage) plus
  the existing manual/E2E playthrough (session-engine, the-reveal), not new
  gameplay code.
- AC-05 is a design constraint to verify once accounts-identity/02 exists:
  confirm its account record does not require touching `Room.cs` or
  `Room.Entitlements`'s capability-only shape.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: code read of api/src/Rooms/Room.cs + a live join in Lobby - confirm only nickname/variant are present in room state.` |
| AC-02 | `manual + tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (existing, ai-cost-gate/02): confirm Room.Entitlements only ever holds a SessionEntitlements capability set (Room_rejects_a_second_entitlement_capture, CreateRoom_captures_unlocked_ai_entitlement_on_the_room) and that no per-player account field exists.` |
| AC-03 | `tests/*.spec.ts (Playwright smoke, extended): a full join -> lobby -> word entry -> reveal round with no account/login step surfaced.` |
| AC-04 | `web/src/safety/checkWord.test.ts` (existing child-safety coverage) - re-run as regression, not new coverage. |
| AC-05 | `manual: at accounts-identity/02 build time, confirm no diff lands in Room.cs / RoomRegistry.cs.` |

## Dependencies
- session-engine/02 (join with a code and nickname) - this story formalizes
  its existing contract.
- child-safety/01 (the filter this story confirms remains authoritative).
