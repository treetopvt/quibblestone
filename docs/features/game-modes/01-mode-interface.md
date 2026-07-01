# Story: Mode interface (the three axes)

**Feature:** Game Modes Engine  ·  **Status:** Complete

## Context
Build the abstraction once so each new mode is cheap. A mode is defined by three
axes; the engine does the rest (collect words, assemble the reveal). See
[feature.md](./feature.md) and README section 4.

## Acceptance Criteria
- [x] AC-01: A mode is defined by three axes: what the player sees (nothing /
      subject only / progressive story), how they answer (free text / word bank),
      and when the reveal happens (at the end / progressively).
      Implemented as `ModeSeeAxis` / `ModeAnswerAxis` / `ModeRevealAxis` and the
      `ModeConfig` type in `web/src/engine/mode.ts`.
- [x] AC-02: The engine collects words for a template's blanks and assembles the
      final story independently of which mode is active.
      Implemented as `collectWord`/`createCollection` + `toOrderedWords` in
      `web/src/engine/engine.ts`, feeding `assemble()` in `assemble.ts`;
      neither branches on mode identity.
- [x] AC-03: Adding a new mode is expressed as a configuration of the three axes;
      it requires no change to word collection or template assembly.
      Demonstrated by `mode.test.ts` ("allows two modes to differ only by
      configuration, with no shared code change required (AC-03)").
- [x] AC-04: The same template is playable by any mode without modification.
      `mode.ts` declares no dependency on `template.ts`/`Template` shape (see
      file header); `engine.ts`'s `collectWord`/`getBlanks` path is
      mode-agnostic.
- [x] AC-05: Free-text answers pass through the safety filter regardless of mode
      (README section 6).
      Implemented as the injectable `SafetyCheck` hook called from
      `collectWord` whenever `mode.answer === 'free-text'`; skipped only for
      `word-bank` (pre-vetted content), per `engine.test.ts`.

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

## Tests
- AC-01, AC-03, AC-04: `web/src/engine/mode.test.ts` (asserts every axis value
  is expressible, and that two modes can differ purely by config).
- AC-02, AC-05: `web/src/engine/engine.test.ts` (collection independent of
  mode; injectable `SafetyCheck` accept/reject/skip-for-word-bank paths).
- Supporting: `web/src/engine/assemble.test.ts`, `web/src/engine/template.test.ts`,
  `web/src/engine/modes/classicBlind.test.ts` (Classic blind as one concrete
  axis configuration).
