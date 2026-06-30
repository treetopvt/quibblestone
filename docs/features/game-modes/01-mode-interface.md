# Story: Mode interface (the three axes)

**Feature:** Game Modes Engine  ·  **Status:** Not Started

## Context
Build the abstraction once so each new mode is cheap. A mode is defined by three
axes; the engine does the rest (collect words, assemble the reveal). See
[feature.md](./feature.md) and README section 4.

## Acceptance Criteria
- [ ] AC-01: A mode is defined by three axes: what the player sees (nothing /
      subject only / progressive story), how they answer (free text / word bank),
      and when the reveal happens (at the end / progressively).
- [ ] AC-02: The engine collects words for a template's blanks and assembles the
      final story independently of which mode is active.
- [ ] AC-03: Adding a new mode is expressed as a configuration of the three axes;
      it requires no change to word collection or template assembly.
- [ ] AC-04: The same template is playable by any mode without modification.
- [ ] AC-05: Free-text answers pass through the safety filter regardless of mode
      (README section 6).

## Out of Scope
- Implementing modes other than Classic blind (their own stories).
- Progressive reveal and word-bank answering mechanics (later modes) - the
  interface must allow them, but this story does not implement them.

## Technical Notes
- Model the mode as a small config object (the three axes) consumed by a pure
  engine; keep it unit-testable (Vitest).
- The safety-filter call is part of the collection path, so every mode inherits
  it for free.

## Dependencies
- template-model/01-template-schema
