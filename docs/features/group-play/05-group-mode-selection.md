# Story: Group mode selection (the host picks the mode for the room)

**Feature:** Group Play Experience  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** TBD

## Context
Solo can pick any of the four modes (`single-player/02` shipped the picker + the
`SOLO_MODES` registry). Group play cannot: `GameHub.StartRound` hardcodes
`Mode = "classic-blind"` with an explicit "a mode PICKER is out of scope here," and
`GroupRound` renders `FillBlank` with no mode surfaces - so the flagship experience
(the car, different houses) plays one mode of four. This story closes that gap: the
HOST picks the mode when starting a round, and every player's round plays it. The
groundwork already exists - the engine, the `ModeSurfaces` contract (`game-modes/03`),
the solo mode registry, and even a `Mode` field on `RoundStartedDto` (today pinned to
`"classic-blind"`). This is composition and wiring, not new engine work. See
[feature.md](./feature.md), `single-player/02-solo-mode-picker.md` (the registry this
generalizes), and `game-modes/03-mode-aware-surfaces.md` (the surface slots).

## Acceptance Criteria
- [ ] AC-01: Given I am the host on the Lobby, when I start a round, then I choose the
      mode for the room from a host-only picker (reusing the solo picker's card visuals
      + the shared mode registry) - big tap targets, single-select. Non-host players do
      not pick; they play whatever the host chose.
- [ ] AC-02: Given the host's chosen mode, when `StartRound` runs, then it takes the
      mode id as a parameter, validates it is a known, offered mode, picks a template
      from THAT mode's eligible set (family-safe honored via the mode's
      `eligibleTemplates`), and broadcasts `RoundStartedDto` with the REAL mode - the
      `Mode` field that already exists on the wire is finally populated for real instead
      of the `"classic-blind"` constant.
- [ ] AC-03: Given the `RoundStarted` broadcast, then every player's `GroupRound`
      resolves the mode id to its `ModeConfig` + `ModeSurfaces` through the SHARED mode
      registry (the one solo uses, generalized so both consume it), renders the right
      `answerSurface` / `seeContext` in `FillBlank` and `revealPresentation` in the
      reveal, and passes the mode's config into `collectWord` - so the collection seam
      is identical for every player and every mode (e.g. Word Bank correctly skips the
      free-text filter, per `game-modes/04`).
- [ ] AC-04 (which modes, first cut): Given the group picker, then it offers the three
      modes that need NO new real-time surface - **Classic Blind, Word Bank, and
      Progressive Reveal** - because each rides the existing distribute -> collect ->
      broadcast-reveal loop unchanged (Word Bank swaps the answer surface; Progressive
      Reveal paces the already-broadcast reveal client-side).
- [ ] AC-05 (Progressive Story deferred - decision, not a gap): Given Progressive Story
      mode, then it is NOT offered in the group picker yet, because its "story so far"
      must reflect OTHER players' in-progress fills - which needs a live partial-fill
      broadcast (a new real-time surface), out of scope here. It is deferred to its own
      story rather than shipped half-working (a player seeing only their own fills as
      "the story so far" in a group would be wrong, not merely limited).
- [ ] AC-06 (child-safety): Given any offered mode, then family-safe still gates which
      templates the mode may draw (per-mode `eligibleTemplates`, at round-start/offering
      time, never a per-tap check), Word Bank picks skip the free-text filter exactly as
      solo (curated, pre-vetted), and this story introduces no new free-text surface and
      no PII (a mode id is not personal data).
- [ ] AC-07 (replay loop): Given "Play another round" (`group-play/04`), then it reuses
      the host's last chosen mode by default (sticky, mirroring the sticky family-safe
      pick), and the host can change the mode when starting the next round - the replay
      never silently resets to Classic Blind.
- [ ] AC-08 (no engine leak): Given this story, then it changes no engine code - it is
      the host picker + one `StartRound` parameter + `GroupRound` wiring, reusing
      `game-modes`' `ModeConfig`/`ModeSurfaces` and the shared registry. If it forces a
      change to `engine.ts`/`assemble.ts`/`FillBlank.tsx`/`Reveal.tsx`, that is an
      abstraction leak - flag it (game-modes feature.md Design notes).

## Out of Scope
- Progressive Story in a group (AC-05) - deferred to its own story with the live
  partial-fill broadcast it needs.
- Per-PLAYER mode within one round - one mode per round, chosen by the host (README
  section 5 assumes one mode per round).
- New modes beyond the four already built, and the Versus/Duel engine stretch (parked
  in game-modes).
- The solo picker (already shipped, `single-player/02`) - this story only generalizes
  its registry so the group can share it.
- Reconnect/rejoin (separate resilience work) - a mid-round mode is round state that
  dies with the round today.

## Technical Notes
- **Generalize the registry, don't fork it.** Lift the mode-to-surfaces mapping out of
  `web/src/pages/soloModes.ts` into a shared registry both `Solo.tsx` and `GroupRound.tsx`
  import (each `SoloMode`-shaped entry is already `config` + `eligibleTemplates` +
  fill/reveal surface builders). Solo's behavior must not change; the group is a second
  consumer of the same list.
- **Server (`GameHub.cs`):** `StartRound` gains a `mode` parameter (default/validate to
  a known mode; reject unknown). Pick the template from the mode's eligible set (mirror
  the per-mode family-safe selection the web registry uses), and populate
  `RoundStartedDto.Mode` with the real id (the DTO field and the `RoundStarted` broadcast
  already exist - `group-play/01`). Keep the offered-mode list in one place server-side
  so an unoffered mode (e.g. progressive-story for now) can never be started.
- **Client (`GroupRound.tsx`):** read `round.mode` (already carried), resolve the
  registry entry, and pass its surfaces into `FillBlank` (reused as-is via `game-modes/03`'s
  optional slots) and its config into the submit path. The Lobby's host Start CTA
  (`session-engine/03` / `group-play/01`) grows the mode picker; keep it host-only.
- **Progressive Reveal in group:** the reveal payload is already broadcast once
  (`group-play/03`); each client paces it locally from the same assembled story - no new
  hub message. **Word Bank in group:** the answer surface + curated word list are
  client-local per blank; distribution/collection are unchanged.
- **Progressive Story (deferred):** note the seam - a future story adds a per-submission
  partial-fill broadcast so every player's story-so-far updates live; only then is it
  offered here.
- Every color/spacing token from `web/src/theme.ts`; FontAwesome only; TS strict.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: host sees a mode picker at round start; a non-host does not |
| AC-02 | API unit test: `StartRound(mode)` validates the mode, picks from its eligible set, broadcasts the real `Mode`; unknown mode rejected |
| AC-03 | manual (2 contexts): host picks Word Bank; both players get the word-bank answer surface and the round plays end to end |
| AC-04 | manual: Classic Blind, Word Bank, Progressive Reveal each playable in a group |
| AC-05 | code review: Progressive Story is not offered in the group picker; the server rejects it |
| AC-06 | code review + manual: family-safe narrows each mode's templates; Word Bank skips the free-text filter; no new text input |
| AC-07 | manual: "Play another round" keeps the chosen mode; the host can change it |
| AC-08 | code review: no edit to `engine.ts`/`assemble.ts`/`FillBlank.tsx`/`Reveal.tsx` |

## Dependencies
- single-player/02-solo-mode-picker (the mode registry + surface-wiring pattern this generalizes)
- game-modes/03-mode-aware-surfaces (the `ModeSurfaces` slots) and game-modes/04, /06 (the modes offered)
- group-play/01-start-round (the `StartRound` + `RoundStarted`/`Mode` seam this extends)
- group-play/03-collect-words, group-play/04-round-complete (the round loop + replay this rides)
- child-safety/02-family-safe-toggle (per-mode eligible-template gating)
