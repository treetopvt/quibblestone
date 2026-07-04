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
| 01 | #60 | Carve it again: same-crew, same-template replay | Complete |
| 02 | #61 | One-blank remix of a finished tale | Complete |
| 03 | #62 | Rotating host ("Pass the chisel") | Complete |

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
- 2026-07-04: Story 02 remix permission - open to ANY player in group play (not
  host-only). Why: the point of a remix is "swap the one word that made *you*
  laugh," so gating it to the host would kill the spontaneity. Every remixed
  word still passes the same server-side safety filter as any submission, so
  open-to-any carries no child-safety cost. Implemented as no host guard on the
  new remix hub method (only a live-room-member check).
- 2026-07-04: Orchestration correction to the Wave Plan - Story 01 does NOT edit
  `GameHub.cs`/`useGameHub.ts` after all: `StartRound(code, familySafe,
  lengthPref, mode, templateId?)` already accepts an explicit pinned template id
  (shipped by story-selection/06's favorites path) and the web `startRound`
  already forwards it, so "Carve it again" is a web-only caller (a secondary
  action on RoundComplete plus one wiring callback in `App.tsx`). Wave structure
  is unchanged (Wave 1 = {01, 02} parallel, Wave 2 = {03}); the serialization
  reasons shift: 01 and 03 now serialize on `RoundComplete.tsx`/`App.tsx`, and
  02 and 03 serialize on `GameHub.cs`/`useGameHub.ts`.
- 2026-07-04: Remix mechanic confirmed as "cherry-pick one blank, submit a new
  word, re-reveal with only that word swapped" (story 02 as built). A one-tap
  "shuffle the existing words into other slots" variant was considered and
  dropped (not parked) - the targeted single-word remix is coherent
  (category-preserving), already verified, and the "jumble" name is reserved for
  the planned AI Fresh Runes feature.
- 2026-07-04: All three stories built via the orchestration playbook (one
  builder per story on isolated worktrees, serial gated integration onto the
  `claude/orchestrate-replay-remix-w1efcd` umbrella). Wave 1 = {01, 02} parallel,
  Wave 2 = {03}. Gate 2 green (API build 0/0, 323 web unit tests incl. the new
  `remixHelpers.test.ts`, web build clean). Verified in two browser contexts:
  carve-it-again restarts both devices on the pinned template (host-only); a
  remix syncs joiner -> host live (AC-07); pass-the-chisel moves the crown + host
  controls live and is host-only + server-enforced.
