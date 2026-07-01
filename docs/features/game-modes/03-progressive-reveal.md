# Story: Progressive reveal ("Whisper mode")

**Feature:** Game Modes Engine  ·  **Status:** Not Started  ·  **Issue:** #52

## Context
README section 5 names Progressive reveal as one of the engine's core
variations: "story is revealed as it goes; you fill the current blank without
knowing what comes next. With or without a word bank." Where Classic blind hides
the whole story until the end, Whisper mode lets the tale unfold live - each
player sees everything written so far and only writes into the next gap, then
watches their word land in the story before the next blank appears. It is the
same engine, configured differently on the `see` and `reveal` axes (`mode.ts`).
See [feature.md](./feature.md) and README section 4 (the three axes).

## Acceptance Criteria
- [ ] AC-01: Given Whisper mode, when I am prompted for a blank, then I see the
      story-so-far rendered up to (but not including) the current blank -
      literal text plus every already-filled word, using the same coral
      word-highlight treatment as the-reveal - directly above the FillBlank
      prompt card; I do not see any text after the current blank.
- [ ] AC-02: Given I submit a word for the current blank, then the story-so-far
      view updates to include my word in place (coral, in the assembled
      position) before the next blank's prompt appears - the reveal happens
      progressively, one blank at a time, not all at once at the end.
- [ ] AC-03: Given Whisper mode, then it is expressed purely as a `ModeConfig`
      (`see: 'progressive-story'`, `reveal: 'progressively'`) - no new branch is
      added to `engine.ts`'s `collectWord`/`assembleStory` or to `assemble.ts`;
      the progressive view is produced by calling the existing `assembleStory`
      (or `assemble`) against the collection-so-far, same as any other mode.
- [ ] AC-04: Given Whisper mode is played with free-text answers, then my
      submitted word passes the safety filter before it is recorded or shown in
      the story-so-far view (same collection-path seam every mode inherits, per
      `game-modes/01` AC-05) - no player ever sees an unfiltered word, even
      transiently, in the progressive reveal.
- [ ] AC-05: Given Whisper mode is played with a word bank (`answer:
      'word-bank'`, per `game-modes/04`), then the same progressive story-so-far
      view renders correctly - the `see`/`reveal` axes and the `answer` axis
      combine independently, proving no mode is a special case of another.
- [ ] AC-06: Given the last blank in the template is filled, then the
      story-so-far view already equals the full assembled story - there is no
      separate "final reveal" transition required, though group play may still
      route to the Reveal screen (`the-reveal/01`) as the shared celebratory
      moment (confetti, reactions) after the last word lands.

## Out of Scope
- A typing/waiting indicator showing what another player is doing before their
  word lands (a delight-tier nicety, not required to prove the axis).
- Word-by-word "carving" entrance animation for the progressive text (that is
  `reveal-delight/02` - this story can ship with a plain, non-animated append
  and let 02 layer the animation on afterward, since both consume the same
  `buildRevealParts`-shaped output).
- Letting a player skip ahead to see unfilled future blanks (defeats the mode).
- Multi-player distribution/ordering of WHO answers which progressive blank -
  that is `group-play/02`'s round-robin; this story only proves the engine/UI
  can render a progressive view, reusing whatever distribution already exists.

## Technical Notes
- Engine: no change to `web/src/engine/engine.ts` or `web/src/engine/assemble.ts`
  is expected (AC-03). The progressive story-so-far view is just
  `assembleStory(template, collectedSoFar)` called against a **partial**
  `CollectedWords` map - `assemble()` is already documented as non-throwing on a
  fewer-words-than-blanks mismatch (see `assemble.ts` header), which is exactly
  the partial-collection case this mode relies on. If building this mode ever
  requires touching `collectWord`, `assembleStory`, or `template.ts`, stop and
  flag it as an abstraction leak (`game-modes/01`'s own gotcha).
- Add a new `ModeConfig` value (mirrors `classicBlind.ts`'s shape) in
  `web/src/engine/modes/`, e.g. `progressiveReveal.ts`, with `see:
  'progressive-story'`, `reveal: 'progressively'`. `answer` is independent
  (free-text or word-bank) - do not hardcode it into this mode's identity;
  offer both a free-text and a word-bank variant, or make `answer` a parameter,
  matching how README section 5 describes it ("with or without a word bank").
- UI: the FillBlank screen (`web/src/pages/FillBlank.tsx`) currently renders an
  optional `subject` string for `see: 'subject-only'`. This mode's `see:
  'progressive-story'` needs a richer slot - render the story-so-far using the
  SAME `buildRevealParts`-style interleave-and-highlight approach already
  proven in `web/src/pages/revealParts.ts` / `Reveal.tsx` (reuse the pure
  helper against the partial assembly, do not reinvent word highlighting).
  Consider whether this becomes a new optional `storyPreview` prop on
  `FillBlankProps` or a small wrapper component composing FillBlank with the
  preview above it - the parent (group-play, later) decides, per FillBlank's
  existing transport-agnostic/composition contract (see the file's header
  comment).
- Real-time: in group play this mode needs every player's screen to update as
  ANY player's word lands (not just their own), since the shared story-so-far
  is common state. That is a `group-play`-owned broadcast concern (a
  `StorySoFarChanged`-shaped hub event alongside the existing `RosterChanged`
  pattern in `api/src/Hubs/GameHub.cs` / `web/src/signalr/useGameHub.ts`), out
  of this story's file footprint - this story's job is the engine config + the
  FillBlank-side rendering, consumed by whichever group-play story wires the
  live broadcast when Whisper mode is scheduled for build.
- Child safety: same seam as Classic blind - the safety check lives on
  `collectWord`'s collection path, so this mode inherits it automatically
  (AC-04); nothing new to wire here.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: FillBlank in Whisper mode shows story-so-far text up to the current blank, nothing after |
| AC-02 | manual: submitting a word updates the story-so-far view with the new word highlighted before the next prompt renders |
| AC-03 | `web/src/engine/modes/progressiveReveal.test.ts` - asserts the mode is a data literal and that `assembleStory` is called unmodified against a partial collection (no new engine branch) |
| AC-04 | `web/src/engine/engine.test.ts` (existing `collectWord` safety-hook coverage) exercised with this mode's `answer` axis |
| AC-05 | manual: Whisper mode combined with a word-bank answer source renders the same progressive view |
| AC-06 | manual: after the last blank, story-so-far view equals `assembleStory` on a complete collection |

## Dependencies
- game-modes/01-mode-interface
- game-modes/02-classic-blind (FillBlank screen to extend/compose)
- template-model/01-template-schema
- child-safety/01-profanity-filter
- the-reveal/01-text-reveal (reuses the word-highlight rendering approach)
