# Story: Template schema (typed blanks, optional word bank, tags)

**Feature:** Template & Content Model  ·  **Status:** In Progress

## Context
Every game mode plays the same underlying thing: a template with typed blanks.
This story defines that shape so the engine and the modes can build on it. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: A template has a title/subject and body text containing ordered,
      typed blanks (for example: plural noun, verb, adjective, name, place,
      exclamation, number).
- [ ] AC-02: Each blank carries: a category label (displayed as the purple
      category chip on the FillBlank screen, e.g. "ADJECTIVE"), a human-facing
      prompt sentence (e.g. "Give me a silly describing word"), a sub-hint
      (e.g. "Something that describes a thing - anything goes!"), and a short list
      of 3 example "spark" words for that category (e.g. "squishy", "gigantic",
      "sparkly"). See `docs/design/README.md` Screens screen 4 (FillBlank) for
      how these fields render on the prompt card.
- [ ] AC-03: A template may optionally carry a word bank (a list of suggested
      words) for word-bank modes; templates without one are still valid.
- [ ] AC-04: A template carries theme and age-appropriateness tags, usable by the
      family-safe toggle and later by content discovery.
- [ ] AC-05: The schema is mode-agnostic: the same template can be played by any
      mode without modification.
- [ ] AC-06: Given a template and an ordered set of words, then assembling them
      produces the final story text deterministically (each blank replaced in
      order); per-word attribution is preserved so each word is associated with
      the player who submitted it (used in the Waiting progress row and Round
      Complete per-player word counts).

## Out of Scope
- AI-generated templates (Phase 2).
- Owner-curated / per-host word banks (a later game mode).
- Rich media (images, audio) inside templates.
- Dynamic / AI-personalized spark chips per player (design pack Expansion 1;
  Slice 1 spark chips are hardcoded in the template schema).

## Technical Notes
- Define the type in shared, pure TS so assembly is unit-testable (Vitest); keep
  it free of UI/real-time concerns.
- The blank type is a small, extensible enum (adjective / noun / verb / name /
  place / exclamation / number / plural-noun). Category label, prompt, sub-hint,
  and spark words are properties of each blank definition - not derived at
  runtime. This keeps the schema self-contained and easy to hand-author.
- The assembled result should carry per-word attribution (playerSessionId +
  word) so the reveal and round-complete screens can color and count correctly.

## Tests
Pure-logic specs run under the minimal Vitest seed (`web/vitest.config.ts`; the
canonical harness is `platform-devops/01`). Run with `npm run test:unit` in `web/`.
- `web/src/engine/assemble.test.ts` - in-order replacement, determinism (same
  inputs produce the same output), per-word attribution (each filled word maps to
  its `playerSessionId`), and both word-count-mismatch directions (AC-06).
- `web/src/engine/template.test.ts` - `getBlanks` ordering, optional `wordBank`
  validity, and the typed-blank shape (AC-01 / AC-02 / AC-03).

## Dependencies
None.
