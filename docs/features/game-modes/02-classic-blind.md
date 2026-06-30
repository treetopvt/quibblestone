# Story: Classic blind mode

**Feature:** Game Modes Engine  ·  **Status:** Not Started

## Context
The first mode built (README section 5): no story context, fill the blanks blind,
laugh at the reveal. It proves the engine end to end. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given Classic blind, when I play, then I am prompted for each blank by
      its type/prompt only ("a plural noun"), with no surrounding story context.
- [ ] AC-02: Given I answer a blank with free text, then my entry is accepted only
      after it passes the safety filter; a failing word gets a friendly retry.
- [ ] AC-03: Given all blanks are filled, then the assembled story is revealed at
      the end - I have not seen it before the reveal.
- [ ] AC-04: Given Classic blind, then it is expressed as a configuration of the
      mode interface (subject-only view, free-text answers, end reveal) rather
      than a bespoke code path.

## Out of Scope
- Word-bank answering and progressive reveal (later modes).
- Multi-player blank distribution (that is group-play); this story is the mode's
  single-filler mechanics, reused by both single-player and group-play.

## Technical Notes
- Implement as engine config (axes from 01-mode-interface). The reveal rendering
  is the-reveal/01; this story produces the collected words + assembled result.

## Dependencies
- game-modes/01-mode-interface
- template-model/01-template-schema
- child-safety/01-profanity-filter
