# Story: Solo mode picker (choose a mode, play it)

**Feature:** Single-Player Experience  ·  **Status:** Complete

## Context
game-modes/03-06 shipped three new modes (Word Bank, Progressive Story,
Progressive Reveal) as file-disjoint plug-ins - each a `ModeConfig` plus a
`ModeSurfaces` value - but deliberately deferred the picker that SELECTS a mode
and wires its surfaces into a live round (every one of those stories lists "the
mode picker ... is out of scope"). So today the three modes are unreachable: solo
always plays Classic blind (`Solo.tsx` hardcodes the `classicBlind` config). This
story is the first consumer of the `ModeSurfaces` contract: a solo player picks
one of the four modes at setup, and the round plays that mode end to end. Solo is
the thin, immediately-verifiable slice for mode selection; group-play mode
selection (host picks at round start, broadcast to the room) stays a separate,
heavier story. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given the solo setup screen, then I see a mode picker listing the
      four modes (Classic blind, Word Bank, Progressive Story, Progressive
      Reveal), each with a short blurb and a big tap target; Classic blind is
      selected by default so the existing zero-choice flow still works with one
      tap on Start.
- [x] AC-02: Given I pick a mode and start, then the round plays THAT mode: its
      `ModeConfig` is passed to `collectWord`, and its `ModeSurfaces`
      (`answerSurface` / `seeContext` / `revealPresentation`) are resolved and
      passed into the shared `FillBlank` / `Reveal` screens - Solo does not edit
      those two screens (it uses their game-modes/03 optional slots).
- [x] AC-03: Given Word Bank, then the fill screen replaces the free-text input
      with the template's curated, category-filtered word list, and the tapped
      word is recorded via the SAME `collectWord` path (skipping the free-text
      filter, per `mode.answer === 'word-bank'`); given Progressive Story, then
      the story-so-far renders above the prompt card and updates each blank;
      given Progressive Reveal, then filling is blind (subject-only) and the
      Reveal paces the finished story one word at a time.
- [x] AC-04: Given Word Bank is selected, then only templates that actually have
      a usable word bank are drawn (via `offerWordBankTemplates`), so the mode
      never renders an empty list or crashes; if the family-safe toggle leaves a
      mode with no eligible template, that mode's card is disabled (not a dead
      Start button), and if the currently-selected mode becomes ineligible the
      selection falls back to Classic blind.
- [x] AC-05: Given any mode, then child safety is unchanged: free-text modes
      (Classic, Progressive Story, Progressive Reveal) still route every
      submission through `collectWord`'s safety check; Word Bank legitimately
      skips the free-text filter (pre-vetted curated content) and is family-safe
      gated at offering time; no mode renders an unfiltered word, and no PII is
      collected.
- [x] AC-06: Given Classic blind is selected (the default), then the fill and
      reveal screens render byte-for-byte as before this story (no surfaces
      supplied) - a pure additive change, proven by the picker defaulting to it.
- [x] AC-07: The picker + wiring is expressed as a small registry
      (`web/src/pages/soloModes.ts`) pairing each existing `ModeConfig` with its
      blurb, eligible-template source, and surface factories - no new engine
      code, no fork of `FillBlank`/`Reveal`, no new `ModeConfig`.

## Out of Scope
- Group-play / multiplayer mode selection (host picks a mode at round start and
  broadcasts it to the room) - a separate, heavier story that touches
  `GameHub.cs` + shared round state. This story is solo only.
- Per-player mode selection within one round, and any NEW mode or new axis value
  (this story only wires the four already-built modes).
- Owner-curated word bank (#54) and Versus/Duel (#55) - parked in game-modes.
- Any change to the engine (`engine.ts`, `assemble.ts`, `mode.ts`, `template.ts`)
  or to `FillBlank.tsx` / `Reveal.tsx` - if wiring a mode needs one, that is an
  abstraction leak to flag, not patch.

## Technical Notes
- New file `web/src/pages/soloModes.ts`: a pages-layer registry of `SoloMode`
  entries. Each pairs an existing `ModeConfig` (`classicBlind` / `wordBank` /
  `progressiveStory` / `progressiveReveal`) with (a) a player-facing blurb +
  FontAwesome icon, (b) an `eligibleTemplates(library, familySafeOn)` selector
  (`selectTemplates` for free-text modes, `offerWordBankTemplates` for Word
  Bank), and (c) `fillSurfaces` / `revealSurfaces` factories that call the
  colocated surface factories (`wordBankSurfaces` / `progressiveStorySurfaces` /
  `progressiveRevealSurfaces`) with the round's runtime context. Classic blind
  returns the `classicBlindSurfaces` no-op (`{}`) for both.
- `Solo.tsx` gains a `mode` state (default Classic blind), renders the picker in
  its existing `SoloSetup` block, draws the template from the selected mode's
  eligible set, passes `mode.config` to `collectWord`, and resolves
  `fillSurfaces` / `revealSurfaces` into `FillBlank` / `Reveal`. It never edits
  those two screens.
- Styling via the MUI theme only (no hex/px literals); FontAwesome icons only; TS
  strict (no `any`, no non-null `!`).

## Tests
- Unit (Vitest) `web/src/pages/soloModes.test.ts`: the registry has all four
  modes with the expected `ModeConfig` axes; `eligibleTemplates` filters
  correctly (Word Bank excludes bank-less templates and honors family-safe;
  free-text modes use `selectTemplates`); a `findSoloMode(id)` lookup resolves
  each mode and returns Classic blind for an unknown id.
- Manual (main-session playthrough): pick each mode on the solo setup screen,
  confirm Word Bank shows tappable curated words, Progressive Story shows the
  story-so-far building up, Progressive Reveal paces the reveal, and Classic
  blind is unchanged.

## Dependencies
- game-modes/03-mode-aware-surfaces (the `ModeSurfaces` slots)
- game-modes/04-word-bank, game-modes/05-progressive-story,
  game-modes/06-progressive-reveal (the three plug-in modes)
- single-player/01-solo-play (the solo state machine this extends)
- child-safety/02-family-safe-toggle (word-bank offering gate)
