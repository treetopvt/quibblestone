# Story: Text reveal

**Feature:** The Reveal  ·  **Status:** Not Started

## Context
Once the words are in, the assembled story is revealed - the moment everyone has
been waiting to laugh at. Text only for Slice 1. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given all words are collected, when the reveal runs, then the
      assembled story (template text with blanks replaced by the submitted words)
      is shown.
- [ ] AC-02: Given the reveal, then the submitted words are visually distinguished
      from the template text (for example, highlighted) so the inserts pop.
- [ ] AC-03: Given the reveal is shown to a room, then every player sees the same
      assembled story.
- [ ] AC-04: Given any submitted word appears in the reveal, then it has passed
      the safety filter (no unfiltered free text is ever rendered).
- [ ] AC-05: Given Slice 1, then the reveal is text (a simple animation is fine);
      there are no voices and no images.

## Out of Scope
- Text-to-speech / character-voice narration (delight tier).
- AI illustrations and share/keepsake export (delight tier).

## Technical Notes
- Render the deterministic assembly from template-model; style with the MUI theme
  so the highlighted inserts stand out (big, playful - README section 10).
- In group play, the reveal is broadcast to the room over SignalR so everyone
  sees it together.

## Dependencies
- template-model/01-template-schema
- game-modes/02-classic-blind
- child-safety/01-profanity-filter
