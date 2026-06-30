# Feature: Game Modes Engine

## Summary
The single most important architectural piece: one engine that every game
variation is a thin configuration of. A mode differs only on three axes - what
the player sees, how they answer, and when the reveal happens. Slice 1 builds the
abstraction plus the first concrete mode, Classic blind.

## README reference
README section 4 ("one engine, many thin modes" - the three axes) and section 7
(Epic Map - Phase 1, Game Modes Engine). Slice 1 mode list: section 5
(Classic blind is "first mode built").

## Stories
- [ ] 01 - Mode interface (the three axes)
- [ ] 02 - Classic blind mode

## Dependencies
- template-model (a mode plays a template).
- child-safety (free-text answers are filtered regardless of mode).

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
