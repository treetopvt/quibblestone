# Story: One-blank remix of a finished tale

**Feature:** Replay & Remix  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
A tiny change to a tale that already got a big laugh is an instant second laugh
- swap just the one word everyone remembers and re-read the whole thing. This
story re-reveals the SAME finished tale with a single blank re-collected: pick
one blank, get a new word for just that blank, and deterministically re-run
the assembly. It stays entirely engine-level (the existing `collectWord` /
`assembleStory` pair from `web/src/engine/engine.ts`), not a special case bolted
onto the Reveal screen. See [feature.md](./feature.md) and
`docs/features/the-reveal/01-text-reveal.md`.

## Acceptance Criteria
- [ ] AC-01: Given the Reveal screen for a completed tale, then I see a
      low-emphasis "Remix a word" action (secondary weight, not competing with
      the existing gold "Play another round" CTA) that lets me pick ONE blank
      from the just-revealed story to re-fill.
- [ ] AC-02: Given I tap "Remix a word", then I see the list of blanks from the
      finished tale (e.g. by their category label and current word, such as
      "adjective: squishy"), and I choose exactly one to remix.
- [ ] AC-03: Given I choose a blank to remix, then I am prompted for a new word
      for that blank only, using the same FillBlank-style prompt card the
      engine already uses for normal collection - not a new UI pattern.
- [ ] AC-04: Given I submit the new word for the remixed blank, when assembly
      re-runs, then the story is deterministically re-assembled with every
      OTHER blank's word unchanged and only the remixed blank's word swapped -
      calling the same `collectWord` + `assembleStory` pair the engine already
      uses for normal rounds, not a parallel re-implementation.
- [ ] AC-05: Given the remixed story is assembled, then the Reveal screen
      re-renders it the same way it renders any assembled story (coral
      highlight on every filled word, including the newly remixed one) - this
      story does not fork or duplicate `Reveal.tsx`'s rendering path.
- [ ] AC-06: Given the new word submitted for the remixed blank, then it passes
      the safety filter before it is recorded or shown, exactly like any other
      free-text submission (solo: the engine-boundary check; group: the
      server-side check).
- [ ] AC-07: Given group play, when one player remixes a blank, then every
      player in the room sees the re-assembled story update together in
      near-real-time over the one SignalR connection - the remix is a shared
      moment, not a private edit only the remixer sees.

## Out of Scope
- Remixing more than one blank in a single action (one blank per remix, by
  design - it is a small, cheap surprise, not a full re-round).
- A visible history of prior remix variants of the same tale (parked in
  `feature.md` - "Remix chains").
- Voting on which blank to remix - whoever taps "Remix a word" picks (in group
  play, this is open to any player unless a later decision restricts it to the
  host; see Technical Notes).
- Changing the template itself, the number of blanks, or any engine contract
  (`assemble()`, `collectWord()`, the mode interface) - if this story needs to
  touch any of those signatures, that is a flag-worthy abstraction leak, not a
  normal implementation detail.
- Solo-play wiring specifics beyond "the same engine calls work the same way
  locally" (solo already runs the engine in-tab per `Solo.tsx`; this story's
  engine-level design applies there for free, but the group-play hub broadcast
  in AC-07 is the part that needs new wiring).

## Technical Notes
- Purely a **new caller of existing engine functions**: hold the previously
  collected `CollectedWords` map (or reconstruct it from `AssembledStory.filledWords`
  + the template), call `collectWord` again for just the chosen `blankId` with
  the new submission (this OVERWRITES the prior entry in the `Map`, since
  `collectWord` keys by blank id - see `web/src/engine/engine.ts`'s file header
  on why collection is keyed by id, not array order), then call `assembleStory`
  again to get a fresh, deterministic `AssembledStory`. No new engine function
  is needed; this story is 100% composition.
- Group play: needs a hub message (e.g. extending the existing round/reveal
  broadcast pattern from `group-play/03-collect-words`) so the remix submission
  and the re-assembled story reach every player in the room, the same way the
  original reveal broadcast does. This is the one piece of new hub surface;
  keep it as a thin sibling of the existing submission + broadcast methods,
  not a new subsystem.
- Reuses `web/src/pages/Reveal.tsx` as-is for rendering; reuses the FillBlank
  prompt-card styling (stone-tablet card, carved input, category chip) for the
  single-blank re-entry step rather than inventing new chrome.
- Decide (and record in this story before building, or in `feature.md`'s
  Decisions log if it changes team-wide behavior) whether "who can remix" in
  group play is open to any player or host-only; either is a small guard on
  the new hub message, not an architecture question. Default assumption for
  planning: open to any player, since the whole point is "pick the blank that
  made *you* laugh," but confirm before implementation.
- The blank-picker list (AC-02) reads from `assembled.filledWords` (already
  has blankId + word + attribution) joined against `template.body` for the
  category label - no new data model.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: Reveal screen shows "Remix a word" as a secondary action, not competing visually with "Play another round" |
| AC-02 | unit (Vitest): a pure helper that lists remixable blanks from `AssembledStory` + `Template` returns the correct category/word pairs |
| AC-03 | manual: the single-blank prompt reuses the FillBlank stone-tablet input pattern |
| AC-04 | unit (Vitest): `web/src/engine/engine.test.ts`-style test proving a second `collectWord` call for the same `blankId` overwrites only that entry and `assembleStory` produces a new result with every other word unchanged |
| AC-05 | manual: remixed story renders through the unmodified `Reveal.tsx`, coral highlight present on the new word |
| AC-06 | manual: submit a filtered word as the remix, confirm rejection with the same friendly retry message |
| AC-07 | manual: two browser contexts confirm both see the re-assembled story update without a refresh |

## Dependencies
- the-reveal/01-text-reveal
- game-modes/01-mode-interface (the engine's `collectWord` / `assembleStory`)
- group-play/03-collect-words (the broadcast pattern this story's hub message mirrors)
- child-safety/01-profanity-filter
