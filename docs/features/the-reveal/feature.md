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
- [ ] 01 - Text reveal

## Dependencies
- template-model (assembling the final story).
- game-modes (the collected words).
- child-safety (no unfiltered word appears in the reveal).

## Design notes
- The reveal renders the deterministic assembly from template-model. Submitted
  words should visually pop (highlighted) against the template text - the contrast
  is where the funny lands.
- Keep it text for Slice 1. The feature is designed to "grow later" (voices,
  images, share/keepsake) without re-architecting the reveal of the text itself.
