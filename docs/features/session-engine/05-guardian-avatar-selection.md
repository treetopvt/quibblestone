# Story: Guardian avatar selection at join

**Feature:** Session & Room Engine  ·  **Status:** In Review

## Context
When joining a room, a player picks one of the six Guardian variants as their
in-session identity. The chosen avatar travels with the player on the Lobby
roster, the Waiting progress row, and the Round Complete recap - so everyone in
the room can tell who's who at a glance. This is anonymous (no PII - just a
nickname and a Guardian variant); it is the child-privacy posture. See
[feature.md](./feature.md) and `docs/design/README.md` Screens - screen 2
(Join) and Shared Component: Guardian.

## Acceptance Criteria
- [x] AC-01: Given I am on the Join screen, then I see a 3-column grid of the
      six Guardian variants; one is pre-selected by default (teal, per the design
      spec). See `docs/design/README.md` Screens screen 2 and
      `docs/design/screens/02-join.png`.
- [x] AC-02: Given I tap a Guardian tile, then that variant becomes selected
      (single-select); the selected tile shows a gold ring (`3px solid #FFB22E`,
      inset -3px, radius 25) and a gold 24px check badge at the top-right that
      pops in with a ~0.25s scale animation.
- [x] AC-03: Given I submit the Join form (code + display name + chosen variant),
      then my chosen Guardian variant is recorded as part of my in-session
      identity and broadcast to the room alongside my nickname.
- [x] AC-04: Given I am in the room, then every player sees me represented by
      the Guardian variant I chose; names and variants are consistent across
      Lobby, Waiting, and Round Complete screens.
- [x] AC-05: Given my display name, then it is checked by the safety filter
      before it is shown to anyone (nickname is free text); a failing name is
      rejected with a friendly message and I can try again. No PII is collected -
      only the in-session nickname and Guardian variant.
- [x] AC-06: Given the Join screen, then a reassurance line reads "100% anonymous
      - no email, no account" (with a shield icon), and no email, password, or
      account field appears.

## Out of Scope
- Persisting the avatar choice across sessions (device-local remembered profile
  is Phase 2, parked).
- Custom avatar upload or creation.
- More than 6 variants in Slice 1.
- Guardian animation on the selection grid (the component is static; idle
  animations are a delight-tier pass).

## Technical Notes
- Web: the avatar grid lives on the Join screen (story 02 Join form). This
  story adds the `selectedVariant` state (defaults to `teal`), grid rendering,
  and sends the variant to the server on join.
- API/Hub: extend the player model to include `variant` (a small string enum:
  `purple | gold | coral | teal | sand | plum`). The server stores it in
  in-memory room state and broadcasts it in roster updates.
- Selection ring + badge pop: use `transform: scale` only for the entrance
  animation (see design pack Gotchas - never animate opacity on a list item).
- Guardian tiles: 78x78px, `border-radius:22px`, background tinted by the
  variant's accent color. Use `<Guardian variant size={52} />` inside each tile.
- See `docs/design/README.md` Screens screen 2, State section (name + variant),
  and `docs/design/screens/02-join.png`.
- Safety filter for the nickname is already implemented by
  child-safety/01-profanity-filter; this story wires it at the join call
  (session-engine/02-join-with-code already specifies this AC; this story adds
  the variant to the same hub call).

## Tests
- `tests/QuibbleStone.Api.Tests/GameHubJoinTests.cs` (xUnit): an unknown or
  malformed variant normalizes to `teal` server-side and a known variant (e.g.
  `coral`) is preserved (AC-03) - the six-variant whitelist is authoritative, so
  a malformed client cannot inject an arbitrary variant string.
- The avatar-grid UI (3-column grid, single-select gold ring + check-badge pop,
  default teal - AC-01/02) is covered by the Phase 4 browser walkthrough
  (Vitest is pure-logic only; there is no component-render harness in Slice 1).

## Dependencies
- session-engine/02-join-with-code
- design-system/02-guardian-component
- child-safety/01-profanity-filter
