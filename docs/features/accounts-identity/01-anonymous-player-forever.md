# Story: Anonymous player, forever

**Feature:** Accounts & Identity  ·  **Status:** Not Started  ·  **Issue:** #67

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

## Acceptance Criteria
- [ ] AC-01: Given a player joins a room with a code and nickname (the
      existing session-engine flow), when the join completes, then the room's
      player record contains only the nickname and chosen Guardian variant -
      no email, no device identifier tied to a person, no account reference of
      any kind.
- [ ] AC-02: Given the server-side room/player model, when it is inspected,
      then there is exactly one boolean-shaped seam - "does this session have
      a signed-in purchaser?" - and it defaults to false; there is no
      per-player account field, login prompt, or optional-sign-in affordance
      anywhere in the join or lobby flow.
- [ ] AC-03: Given a room with no signed-in purchaser at all (the common
      case), when the room plays a full round end to end (join, lobby, word
      entry, reveal), then nothing in that flow requires, prompts for, or
      references an account - free play is provably login-free.
- [ ] AC-04: Given the nickname is free text, when it is submitted, then it
      still passes the existing safety filter (child-safety/01) before it is
      stored or shown to anyone - this story does not weaken or bypass that
      check.
- [ ] AC-05: Given this story lands, when accounts-identity/02 and 03 are
      built later, then they add a *new*, separate purchaser-account record
      (keyed independently, per feature.md) rather than modifying the
      player/room model from AC-01 - confirmed by a code-level check (no
      changes to `Room.cs` / `RoomRegistry.cs` are required to support
      accounts).

## Out of Scope
- Any UI change to Join, Lobby, or the join flow - this is a documentation-
  and-hardening pass over an existing, working contract, not a new screen.
- The purchaser account itself (accounts-identity/02).
- Remembering a player's nickname/Guardian across devices or sessions -
  players remain anonymous and stateless between rooms (README section 3;
  also called out as parked in session-engine/feature.md).
- Any change to the safety filter itself (child-safety owns that).

## Technical Notes
- This is primarily a **contract-and-comment** story: the existing
  `api/src/Rooms/Room.cs` and `api/src/Rooms/RoomRegistry.cs` (session-engine/01)
  already store only nickname + Guardian variant per player. Add or extend the
  header-comment block on `Room.cs` to explicitly document "this record is
  PII-free by design; do not add an account/device/email field here - a
  purchaser account, if any, lives in a separate record (accounts-identity)."
  This matches the verbose-header-comment convention already in the tree (see
  `RoomRegistry.cs`).
- If a "does this session have a signed-in purchaser?" seam does not yet exist
  anywhere, this story is the one that names it (e.g. a single nullable
  purchaser-reference field on the room, defaulted to null/absent) - but does
  **not** wire it to anything yet. It exists purely so
  billing-entitlements/01's session-creation check has a single, obvious place
  to read from later, per the reuse map in implementation.md.
- No API endpoint, no hub method, and no web component changes are expected
  for AC-01 through AC-04 - they are true today. Verification is a targeted
  code read plus the existing manual/E2E playthrough (session-engine,
  the-reveal), not new gameplay code.
- AC-05 is a design constraint to verify once accounts-identity/02 exists:
  confirm its account record does not require touching `Room.cs`.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: code read of api/src/Rooms/Room.cs + a live join in Lobby - confirm only nickname/variant are present in room state.` |
| AC-02 | `manual: code read confirming a single, defaulted seam exists and no per-player account field does.` |
| AC-03 | `tests/*.spec.ts (Playwright smoke, extended): a full join -> lobby -> word entry -> reveal round with no account/login step surfaced.` |
| AC-04 | `web/src/safety/checkWord.test.ts` (existing child-safety coverage) - re-run as regression, not new coverage. |
| AC-05 | `manual: at accounts-identity/02 build time, confirm no diff lands in Room.cs / RoomRegistry.cs.` |

## Dependencies
- session-engine/02 (join with a code and nickname) - this story formalizes
  its existing contract.
- child-safety/01 (the filter this story confirms remains authoritative).
