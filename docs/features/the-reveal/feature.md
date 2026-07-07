# Feature: The Reveal

## Summary
The payoff moment: the completed story is revealed for everyone to laugh at. Slice
1 is text only (a simple animation is fine) - no voices, no images. The host
reads it aloud themselves; TTS and illustrations come in the delight tier.

## README reference
README section 7 (Epic Map - Phase 1, The Reveal) and section 8 (Slice 1: "text
reveal only, no voices, no images"). Design importance: section 10 ("the payoff
moment ... deserves the most love").

## Stories
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #34 | Text reveal | Complete |

## Dependencies
- template-model (assembling the final story).
- game-modes (the collected words).
- child-safety (no unfiltered word appears in the reveal).
- design-system (the Reveal screen uses the theme, stone-tablet styling, and
  shared button + AppBar components).

## Design notes
- The reveal renders the deterministic assembly from template-model. Submitted
  words are highlighted coral (`#FF6B57`) against the Nunito 600 body text - the
  visual contrast is where the funny lands. See `docs/design/README.md` Screens
  screen 6 for the full coral-word styling spec.
- Keep it text for Slice 1. The feature is designed to "grow later" (voices,
  images, share/keepsake) without re-architecting the reveal of the text itself.
- The Reveal screen includes a narration bar (play/pause FAB, waveform, label)
  whose hooks are reserved in Slice 1 (visible but inactive). Phase 3 wires TTS
  without a layout change.

## Parked - Phase 3
- Character-voice TTS narration and the animated waveform (design pack Expansion
  4 and Screens screen 6 narration bar). The narration bar is rendered in Slice 1
  but inactive; TTS is wired in Phase 3.
- Word-by-word "carving" reveal animation - each word fades/scales in
  sequentially as the stone "carves" (design pack Expansion 4).
- Saving and sharing the finished tale as an image of the tablet (design pack
  Expansion 4).

## Parked - Phase 4
- Reaction row: Laugh / Heart / Wow / Star pill buttons with tap-to-increment
  counts and floating icon animation (design pack Screens screen 6, Interactions
  - Reactions).
