# Feature: Template & Content Model

## Summary
The data model for the game's content: a template is a piece of text with
ordered, typed blanks, an optional word bank, and theme/age tags. Plus a simple
authoring format and a tiny hand-written seed library (10-15 stories, no AI yet).

## README reference
README section 7 (Epic Map - Phase 1, Template & Content Model) and section 8
(Slice 1: "a tiny hand-written library, 10-15 stories, no AI yet"). The model is
mode-agnostic to support section 4's "one engine, many thin modes".

## Stories
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #25 | Template schema (typed blanks, optional word bank, tags) | Complete |
| 02 | #26 | Authoring format + seed library | Complete |

## Dependencies
None (this is the content foundation). child-safety vets the seed content.

## Design notes
- The schema must be **mode-agnostic**: the same template is playable by any game
  mode (the mode only decides what the player sees, how they answer, and when the
  reveal happens). This is what keeps new modes cheap (README section 4).
- Assembling a completed template with a set of words must be deterministic
  (blanks replaced in order) - this is the pure logic the reveal renders and the
  natural thing to unit-test (Vitest).
- Slice 1 content is hand-written and family-safe; the AI Content Factory
  (Phase 2) generates more later. Keep the authoring format human-friendly so
  hand-authoring is not a chore.
