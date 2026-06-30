# Story: Classic blind mode

**Feature:** Game Modes Engine  ·  **Status:** Not Started

## Context
The first mode built (README section 5): no story context, fill the blanks blind,
laugh at the reveal. It proves the engine end to end. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given Classic blind, when I am prompted for a blank, then I see:
      a progress row ("Word N of M" with a chisel icon + "X to go" in teal) and
      an 8-segment progress bar (completed/current segments gold, current segment
      glowing, upcoming segments sand `#DFD2B4`). See `docs/design/README.md`
      Screens - screen 4 (FillBlank) and `docs/design/screens/04-fillblank.png`.
- [ ] AC-02: Given Classic blind, then the blank prompt is shown on a stone-
      tablet card (arched, carved rim, glow) containing: a centered category chip
      (purple pill, sparkle icon, uppercase label e.g. "ADJECTIVE"); a prompt
      sentence in Fredoka 600 29px where the category word is colored purple (e.g.
      "Give me a silly describing word"); and a sub-hint in Nunito 700 muted text
      explaining the category (e.g. "Something that describes a thing - anything
      goes!"). There is no surrounding story context visible. See
      `docs/design/README.md` Screens screen 4.
- [ ] AC-03: Given the blank prompt card, then a carved input slot (`#DCCFB0`,
      inset shadow, radius 18, height 66px) with a chisel icon accepts free-text
      input (Fredoka 500 24px, placeholder "type a fun word...", maxLength 20).
      Below the input, an "example spark" row shows "Need a spark?" text and 3
      tappable teal chips with example words for the current category; tapping a
      chip fills the input with that word.
- [ ] AC-04: Given I am on the FillBlank screen, then a blind-mode reassurance
      panel (purple-tint background, eye-off icon) reads "Blind mode - no peeking
      at the story. The big reveal comes at the end!" so I am clear why I cannot
      see the story. See `docs/design/README.md` Screens screen 4.
- [ ] AC-05: Given I have typed or chosen a word, when I tap the gold "Next word
      ->" CTA, then my word is submitted (passing the safety filter first); a
      failing word is rejected with a friendly message and I can try another.
- [ ] AC-06: Given I am stuck on a blank, then a low-pressure ghost link "Stuck?
      Skip this word" (purple, below the gold CTA) allows me to skip the current
      blank; the blank is left empty or receives a default placeholder, and I
      advance to the next.
- [ ] AC-07: Given all my assigned blanks are filled (or skipped), then I
      transition to the Waiting interstitial (group play) or immediately to the
      reveal (solo play); I have not seen the story text at any point.
- [ ] AC-08: Given Classic blind, then it is expressed as a configuration of the
      mode interface (subject-only view, free-text answers, end reveal) rather
      than a bespoke code path.

## Out of Scope
- Word-bank answering and progressive reveal (later modes).
- Multi-player blank distribution (that is group-play/02); this story is the
  mode's single-filler mechanics, reused by both single-player and group-play.
- FillBlank screen background animations (faint carved tablet SVG pulse, floating
  runes) - these are the delight-tier pass.
- The spark chip examples being dynamically driven by template metadata (they
  can be hardcoded per category for Slice 1; a later enhancement ties them to
  the template schema).

## Technical Notes
- Implement as engine config (axes from 01-mode-interface). The reveal rendering
  is the-reveal/01; this story produces the collected words + assembled result.
- FillBlank screen lives in `web/src/` (a page/view). It receives the current
  blank's category, prompt text, sub-hint, and example chips from the engine
  (or the template schema). Sending the submitted word goes to the hub (group
  play) or directly to local engine state (solo play).
- Progress bar: an 8-segment flex row, gap 5px, each segment `height:9px`
  `border-radius:5px`. Current segment uses a CSS `@keyframes` glow pulse (1.8s).
  Use the count of assigned blanks for the total; the segment count adapts.
- Category chip: MUI Chip, purple variant, Nunito 800, uppercase. Example spark
  chips: teal MUI Chips in a row; tapping calls `setWord(chipText)`.
- Skip link: an MUI `<Link>` or ghost button below the primary CTA. The skip
  behavior (empty blank vs placeholder word) is a product decision; for Slice 1
  "skip leaves the blank empty" is acceptable.
- Safety filter is called server-side on submission (group play) or at the
  engine boundary (solo play) before the word is recorded. See
  child-safety/01-profanity-filter.
- See `docs/design/README.md` Screens screen 4 for full layout details.

## Dependencies
- game-modes/01-mode-interface
- template-model/01-template-schema
- child-safety/01-profanity-filter
- design-system/01-mui-theme-and-app-shell
