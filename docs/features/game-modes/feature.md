# Feature: Game Modes Engine

## Summary
The single most important architectural piece: one engine that every game
variation is a thin configuration of. A mode differs only on three axes - what
the player sees, how they answer, and when the reveal happens. Slice 1 builds the
abstraction plus the first concrete mode, Classic blind. This look-ahead pass adds
the next four modes from README section 5, still expressed as configuration on
the same engine.

## README reference
README section 4 ("one engine, many thin modes" - the three axes) and section 7
(Epic Map - Phase 1, Game Modes Engine; Phase 3, "Remaining Game Modes"). Mode
list: section 5 (Classic blind is "first mode built"; Blind + word bank,
Progressive reveal, Owner-curated word bank are the next three named variations).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #27 | Mode interface (the three axes) | Not Started |
| 02 | #28 | Classic blind mode | Not Started |
| 03 | TBD | Progressive reveal ("Whisper mode") | Not Started |
| 04 | TBD | Blind + word bank | Not Started |
| 05 | TBD | Owner-curated word bank | Not Started |
| 06 | TBD | Versus / Duel mode | Not Started |

## Dependencies
- template-model (a mode plays a template).
- child-safety (free-text answers are filtered regardless of mode).
- design-system (Classic blind screen uses the theme, buttons, and AppBar).
- session-engine and group-play (03-06 are group-shaped modes: they need a
  live roster and a round in flight to mean anything - a word bank, a
  progressive story, or a vote only makes sense with other players present).

## Design notes
- The three axes (README section 4):
  1. What the player sees: nothing / subject only / progressive story
  2. How they answer: free text / word bank
  3. When the reveal happens: at the end / progressively
- Word collection and template assembly belong to the **engine**, not the mode.
  A mode only configures the axes. If adding a mode means touching assembly or
  collection, the abstraction has leaked - fix the abstraction.
- This is what keeps every later mode (progressive reveal, word bank, owner-
  curated bank) days of work instead of weeks.
- Stories 03-05 stay pure axis configuration, same as Classic blind - each is a
  new `ModeConfig` value (see `web/src/engine/mode.ts`) plus, where the axis is
  genuinely new (word-bank answering, a progressive-story view), the one-time
  engine capability that axis value requires. That capability is not "for" any
  one mode; any future mode can turn the same axis value on.
- Story 06 (Versus / Duel) is the one mode in this pass that is honestly a
  **stretch** of the engine, not just a new axis value: multiple players
  answering the SAME blank, plus a room-wide vote on the reveal. The three axes
  do not currently express "how many players answer one blank" or "a vote
  happens after the reveal" - so 06's job is to generalize the engine's
  collection model (many-per-blank, not one-per-blank) and add a lightweight,
  reusable vote-collection primitive, and land BOTH in the engine/mode-interface
  orbit (`web/src/engine/`), never hard-coded to Versus. See 06's Technical
  Notes for the shape. This keeps the "one engine" bet honest: if the
  generalization leaks into a Versus-only code path, that is the abstraction
  failing exactly the way CLAUDE.md section 2 warns about.
- The vote primitive story 06 needs is the same shape `reveal-delight/03`
  (Golden Guardian funniest-word award) needs - both are "the room taps to pick
  a winner among options, tally, show the result." Building it once in the
  engine and having both features consume it avoids two parallel voting
  mechanisms. See `docs/features/reveal-delight/feature.md` and
  `docs/features/reveal-delight/03-golden-guardian.md`.

## Parked - Phase 2+
- More game modes beyond the five named in README section 5 (the axes are
  designed to keep adding modes cheap; new ones are proposed and slotted in as
  they come up, not designed speculatively here).
- Per-player mode selection within a single round (README section 5 assumes one
  mode per round, chosen at round start).
- AI-personalized spark chips / word banks generated per player (template-model
  Phase 2 territory, not a mode concern).

## Decisions
- 2026-07-01: Extended the feature with stories 03-06 (README section 5's
  remaining named modes) as a look-ahead pass ahead of Slice 1 shipping, per the
  "keep the backlog ahead of development" mandate. All four are Status "Not
  Started", Issue "TBD" - they are planned, not scheduled; they park behind
  Slice 1 shipping and (for 03-06) group-play landing, since they all need a
  live room to be meaningful.
- 2026-07-01: Flagged Versus/Duel (06) explicitly as an engine stretch rather
  than pure axis config, so a future builder does not quietly fork the engine
  to ship it. The generalization (many-answers-per-blank + a vote primitive)
  belongs in `web/src/engine/`, shared with `reveal-delight/03`.
- 2026-07-01: Recorded the Versus/Duel judging model in response to a design
  question ("how do we decide the best answer?"): the in-room HUMAN vote is the
  canonical judge (README section 1 - the payoff is people laughing together,
  not an algorithm's verdict). AI scoring is kept only as an optional,
  non-authoritative "Guardian's Verdict" for solo play / a second opinion
  (`ai-on-demand-generation/03`); judging by sharing to an outside person is
  parked. See 06's Out of Scope.
