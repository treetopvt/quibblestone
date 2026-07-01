# Feature: Replay & Remix

## Summary
The "again!" reflex: cheap, fast ways to keep a crew laughing on the same engine
they just used, without re-gathering. A one-tap same-crew replay, a one-blank
remix of a tale they already loved, and a rotating host so the driving seat
moves around the car.

## README reference
README section 1 (the emotional target is "loud, silly, instant fun ... everyone
is laughing within two minutes" - replay is how that keeps happening once the
first laugh lands) and section 8 ("additive on a thing that already works" - this
feature adds nothing to the engine itself, only to how a crew keeps using it).
Builds on `group-play/04-round-complete` (the existing replay loop: "Play another
round" / "Back to lobby").

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | TBD | Carve it again: same-crew, same-template replay | Not Started |
| 02 | TBD | One-blank remix of a finished tale | Not Started |
| 03 | TBD | Rotating host ("Pass the chisel") | Not Started |

## Dependencies
- group-play (the round lifecycle and the Round Complete replay loop this
  feature extends: `startRound`, round numbering, the same-room "Play another
  round" seam).
- session-engine (room + roster; story 03 needs the roster to offer a new host).
- the-reveal (the screen a remix re-renders).
- template-model + game-modes (the engine this feature configures, never forks).
- child-safety (every re-collected word still passes the filter).

## Design notes
- Nothing here is a new engine. Story 01 is `group-play/04`'s existing "Play
  another round" with the template-choice step pinned to the same template
  instead of a fresh pick. Story 02 is the engine's existing collect-one-blank
  path (`web/src/engine/engine.ts`'s `collectWord`) called for a single blank id
  against an already-assembled story, then `assembleStory` re-run - the exact
  same deterministic assembly the-reveal already renders. Story 03 is a host
  flag moving on the existing player record, not a new role model. If any story
  here needs a change to `assemble()`, `collectWord`, or the mode interface,
  that is an abstraction leak - stop and flag it rather than patching around it.
- All three stories run over the **one** SignalR connection: a same-crew replay,
  a remix, and a host handoff are hub messages the room reacts to together, the
  same pattern group-play already established (round start, blank distribution,
  submission, reveal are all hub messages).
- Slice-1-adjacent scope discipline: this is a **look-ahead, post-Slice-1**
  feature. It is genuinely additive (README section 8's "then, additive on a
  thing that already works") - it must not creep into a second engine, a
  scoring system, or a lobby redesign. Each story stays a thin extension of an
  existing hub method or engine call.
- Story ordering matters for build risk: 01 (replay) is the smallest, lowest-risk
  extension of an already-spec'd flow (group-play/04); 03 (host handoff) touches
  authorization (who is allowed to call `startRound`) so it should land after 01
  proves the replay path is solid; 02 (remix) is engine-level and can run in
  parallel with either since it touches neither round lifecycle nor host state.

## Parked - Phase 2+
- Remix chains ("remix the remix") with a visible history of prior variants of
  the same tale - fun, but needs a UI for browsing variants; out of scope until
  there is a place to show that history (see `keepsake-gallery`).
- A "randomize a different template, same crew" quick-replay variant (distinct
  from same-template replay) - parking until it is clear players actually want
  variety over repetition in the replay moment.
- Voting on which blank to remix (currently: the remixer or host just picks
  one) - needs its own small UI and is not worth the round-trip cost yet.
- Host handoff mid-round (story 03 is deliberately between-rounds only, never
  mid-collection) - revisit only if playtesting shows a mid-round dropout is
  common enough to need a live handoff.

## Decisions
- 2026-07-01: Scoped as three independent thin extensions (replay, remix, host
  handoff) rather than one "replay options" mega-story, so each stays
  INVEST-small and testable with two browser contexts on its own. Why: the
  Wave Plan showed 01 and 03 both touch `startRound`/host-authorization in
  `GameHub.cs` (so they must serialize), while 02 is pure engine + Reveal-screen
  wiring with no hub-authorization overlap (so it can run alongside either).
