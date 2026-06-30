# Story: Text reveal

**Feature:** The Reveal  ·  **Status:** Not Started

## Context
Once the words are in, the assembled story is revealed - the moment everyone has
been waiting to laugh at. Text only for Slice 1. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given all words are collected, when the reveal runs, then the
      assembled story (template text with blanks replaced by the submitted words)
      is shown on the Reveal screen: confetti, a "Your tale is carved!" header
      (Fredoka 700 26px with twinkling star glyphs), a byline "carved by [names]
      & crew", and the story in a glowing stone-tablet scroll panel. See
      `docs/design/README.md` Screens - screen 6 (Reveal) and
      `docs/design/screens/06-reveal.png`.
- [ ] AC-02: Given the reveal, then every filled-in word is rendered in coral
      (`color:#FF6B57; font-weight:800; border-bottom:2px solid
      rgba(255,107,87,.4)`) against the Nunito 600 17.5px body text (line-height
      1.72), so the player-supplied words pop visually. See `docs/design/README.md`
      Screens screen 6 (Story scroll).
- [ ] AC-03: Given the reveal is shown to a room, then every player sees the same
      assembled story in near-real-time; the broadcast happens over SignalR so no
      player needs to refresh.
- [ ] AC-04: Given any submitted word appears in the reveal, then it has passed
      the safety filter (no unfiltered free text is ever rendered).
- [ ] AC-05: Given Slice 1, then the reveal is text only; there is no TTS
      narration audio and no AI illustration.
- [ ] AC-06: Given the Reveal screen, then a pinned bottom action bar shows: gold
      "Play another round" CTA (triggers the round-complete / replay flow in
      group-play/04) and secondary outlined-purple "Share the tale" button. The
      story scroll area scrolls independently above the pinned bar and is never
      obscured by it. See `docs/design/README.md` Bottom action bar pattern and
      Screens screen 6.
- [ ] AC-07: Given the Reveal screen has a narration bar (play/pause, waveform,
      label), then in Slice 1 the play button is visible but triggers a "coming
      soon" state or is disabled; the waveform does not animate. The UI real
      estate is reserved so Phase 3 can wire TTS without a layout change.

## Out of Scope
- Text-to-speech / character-voice narration (Phase 3, parked in feature.md).
- Word-by-word "carving" reveal animation (Phase 3, parked in feature.md).
- AI illustrations and share/keepsake export as an image (Phase 3, parked in
  feature.md).
- Reaction row (Laugh / Heart / Wow / Star buttons with floating icon counts)
  (Phase 4, parked in feature.md).
- Confetti physics library - use a lightweight CSS-only approach (8 pieces,
  palette colors, fall+spin via `@keyframes`, 2.6-3.4s alternate). No canvas.

## Technical Notes
- Render the deterministic assembly from template-model. The reveal view is a
  scrollable story body inside the stone-tablet panel. The stone-tablet styling
  (gradient, arched radius `40px...28px`, pulsing glow `@keyframes` 4s
  alternating purple/gold shadow) comes from the MUI theme's shape tokens and a
  shared TabletCard component or sx prop.
- Coral word highlight: wrap each filled-in word in a `<span>` with the coral
  style applied via MUI `sx` or a CSS class; do not use the theme for this color
  - it is a content-level style, not chrome.
- In group play, the reveal broadcast is a hub message that sends the assembled
  story to all players in the room.
- The byline parses the players list from the room state; display names must have
  already passed the safety filter (they did at join time).
- The "Share the tale" button should invoke the Web Share API (same approach as
  session-engine/04) with the story title and text as the share payload. Fall
  back gracefully if Web Share is unavailable.
- See `docs/design/README.md` Screens screen 6 for full layout, and
  Implementation Gotchas for the pinned bottom bar pattern.

## Dependencies
- template-model/01-template-schema
- game-modes/02-classic-blind
- child-safety/01-profanity-filter
- design-system/01-mui-theme-and-app-shell
