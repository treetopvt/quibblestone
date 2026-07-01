# Story: Owner-curated word bank

**Feature:** Game Modes Engine  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
README section 5: "Owner-curated word bank - the round's host supplies the word
bank everyone draws from." This is `game-modes/04`'s Blind + word bank mode with
one change: instead of the word bank coming from the template's authored
`wordBank`, the round's HOST types a short list of words before the round
starts, and that host-supplied list becomes the bank every player draws from
during collection. The rendering, selection, and collection mechanics are
identical to 04 - only the SOURCE of the bank differs, which is why this is a
new story on top of 04 rather than a new axis. See [feature.md](./feature.md)
and README section 5.

## Acceptance Criteria
- [ ] AC-01: Given I am the host and the round is about to start, then I am
      offered a short authoring step: for each blank category the template
      needs, I type in a small set of words (matching the "3 example words per
      category" shape already established by `Blank.sparkWords` /
      `WordBankEntry`) that become the round's word bank.
- [ ] AC-02: Given the host submits their authored words, when the round
      starts, then every player draws from THIS host-authored bank (filtered by
      each blank's category, same as `game-modes/04` AC-02) rather than any
      bank baked into the template - the FillBlank rendering and selection flow
      are otherwise unchanged from 04.
- [ ] AC-03: Given the host's authoring step, then each typed word passes the
      safety filter BEFORE it is added to the round's bank and distributed to
      other players - this is the one place in this mode's flow where free text
      genuinely needs filtering, because a host-typed word is free text, unlike
      a pre-vetted template `wordBank` entry (README section 6). A rejected word
      is shown a friendly inline message and the host can retry, same UX
      contract as `child-safety/01` AC-02.
- [ ] AC-04: Given the family-safe toggle is ON for the session, then the
      host-authoring step is still available (the toggle does not disable
      hosting a bank), but every host-typed word still passes the standard
      safety filter regardless of toggle state (the toggle gates CURATED
      content selection, it never relaxes the free-text filter -
      `child-safety/02` AC-04) - and no PII is collected from the host's
      authored words or from any player who draws from the bank.
- [ ] AC-05: Given the host does not author enough words for a category (fewer
      than the blanks needing it), then the round falls back to the template's
      own `wordBank` entries for that category if the template has one, or the
      mode is simply unavailable for that template if it has neither - the
      round never gets stuck with an empty bank for a category a player needs.
- [ ] AC-06: Given this mode, then it is expressed as `game-modes/04`'s
      `ModeConfig` (`answer: 'word-bank'`) plus a round-level "bank source"
      concept (host-authored vs. template-authored) that lives ALONGSIDE the
      mode config, not inside it - the three axes (`mode.ts`) do not grow a
      fourth value for "who supplied the bank"; that is round setup data, not a
      new mode dimension. This keeps `ModeConfig` itself unchanged.

## Out of Scope
- The host editing or removing individual bank words after the round starts.
- Sharing a host-authored bank across multiple rounds/templates (each round's
  authored bank is scoped to that round only, for Slice-of-this-phase
  simplicity).
- Any UI for the host to preview how their words will read in context before
  the round starts (a delight-tier nicety).
- Solo play offering this mode (there is no "other players" to curate for when
  playing alone - this mode is inherently group-shaped; solo continues to use
  Classic blind or a template-authored word bank per `game-modes/04`).

## Technical Notes
- Reuses `game-modes/04`'s entire FillBlank word-bank rendering and the
  `answer: 'word-bank'` collection path (`engine.ts`'s existing `collectWord`
  skip-safety-check-for-word-bank behavior) UNCHANGED. This story adds exactly
  two new pieces: (1) a host-authoring screen/step, and (2) a round-scoped bank
  source that FillBlank's word-bank renderer reads from instead of
  `template.wordBank`.
- The host-authoring step is the ONE place in this mode where the safety filter
  DOES run (AC-03) - unlike 04's template-authored entries, which are already
  vetted at authoring time. Route each host-typed word through the same
  `IContentSafetyFilter` (`api/src/Safety/`) used everywhere else, server-side,
  before it is added to the round-scoped bank and broadcast to other players -
  never trust a client-side pre-check alone (mirrors `GameHub.cs`'s existing
  `JoinRoom` pattern: validate name -> safety-check -> only then broadcast).
- Distribution: the host-authored bank needs to reach every player's client
  before/at round start - this is a `group-play`-shaped concern (a hub payload
  alongside `startRound`, extending `group-play/01`'s "sets the round's
  template + mode" scope to also carry the bank when this mode is chosen), out
  of this story's own file footprint but noted here so whichever wave builds it
  wires it through the existing round-start broadcast rather than inventing a
  second channel.
- Represent "bank source" as data passed alongside the `ModeConfig`, not as a
  new field ON `ModeConfig` (AC-06) - e.g. the round-start payload can carry an
  optional `hostAuthoredBank: WordBankEntry[]` that FillBlank's word-bank
  renderer prefers over `template.wordBank` when present, with the fallback
  rule in AC-05 implemented as a plain "prefer host bank, else template bank,
  else mode unavailable" function - keep it pure and unit-testable.
- Every color/spacing token from `web/src/theme.ts`; icons from FontAwesome
  only. The host-authoring inputs reuse the same carved-input-slot visual
  language as FillBlank's free-text input (`web/src/pages/FillBlank.tsx`'s
  input styling) so the family recognizes the pattern.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: host sees an authoring step before round start, one word list per category the template needs |
| AC-02 | manual: players draw only from the host-authored bank during the round, filtered by category |
| AC-03 | manual + API-side unit test (mirrors `child-safety/01` coverage) - a profane host-typed word is rejected with a friendly retry, never distributed |
| AC-04 | manual: toggling family-safe on does not disable host authoring; a filtered word is still rejected regardless of toggle state |
| AC-05 | unit test on the pure "prefer host bank, else template bank, else unavailable" selection function |
| AC-06 | code review: `ModeConfig` (`mode.ts`) has no new field; bank source is carried as separate round data |

## Dependencies
- game-modes/04-blind-word-bank
- game-modes/01-mode-interface
- template-model/01-template-schema
- child-safety/01-profanity-filter
- child-safety/02-family-safe-toggle
- group-play (round-start broadcast to carry the host-authored bank)
