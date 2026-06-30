# Story: Template schema (typed blanks, optional word bank, tags)

**Feature:** Template & Content Model  ·  **Status:** Not Started

## Context
Every game mode plays the same underlying thing: a template with typed blanks.
This story defines that shape so the engine and the modes can build on it. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: A template has a title/subject and body text containing ordered,
      typed blanks (for example: plural noun, verb, adjective, name, place).
- [ ] AC-02: Each blank carries the prompt shown to a player ("a plural noun").
- [ ] AC-03: A template may optionally carry a word bank (a list of suggested
      words) for word-bank modes; templates without one are still valid.
- [ ] AC-04: A template carries theme and age-appropriateness tags, usable by the
      family-safe toggle and later by content discovery.
- [ ] AC-05: The schema is mode-agnostic: the same template can be played by any
      mode without modification.
- [ ] AC-06: Given a template and an ordered set of words, then assembling them
      produces the final story text deterministically (each blank replaced in
      order).

## Out of Scope
- AI-generated templates (Phase 2).
- Owner-curated / per-host word banks (a later game mode).
- Rich media (images, audio) inside templates.

## Technical Notes
- Define the type in shared, pure TS so assembly is unit-testable (Vitest); keep
  it free of UI/real-time concerns.
- Blank "types" are a small, extensible set; the prompt string is what the player
  actually sees.

## Dependencies
None.
