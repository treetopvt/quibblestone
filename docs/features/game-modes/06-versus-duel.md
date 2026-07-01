# Story: Versus / Duel mode

**Feature:** Game Modes Engine  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
Two or more players fill the SAME blank instead of splitting the template's
blanks between them; on the reveal, the room votes on which of the competing
answers is funniest. This is the one mode in this look-ahead pass that is
honestly a **stretch** of "one engine, many thin modes" rather than a pure
configuration of the existing three axes (`web/src/engine/mode.ts`): the axes
describe WHAT a player sees, HOW they answer, and WHEN the reveal happens, but
none of them describe HOW MANY players answer one blank, or that a VOTE happens
after the reveal. Versus needs both. The job of this story is to generalize the
engine's collection model just enough to express "many answers, one blank" and
add a small, reusable vote-collection primitive - and land BOTH inside
`web/src/engine/` (the engine/mode-interface orbit), never as a bespoke
Versus-only code path. If either lands hard-coded to Versus, the abstraction has
failed exactly the way CLAUDE.md section 2 warns about - flag it rather than
ship it that way.

The vote primitive this story needs is the SAME shape `reveal-delight/03`
(Golden Guardian funniest-word award) needs: "the room taps to pick a winner
among a small set of options, tally taps, surface the result." Building one
primitive and having both features consume it avoids two parallel voting
mechanisms in the codebase. See
[`docs/features/reveal-delight/03-golden-guardian.md`](../reveal-delight/03-golden-guardian.md)
and [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a Versus blank, when the round is set up, then MORE THAN ONE
      player is assigned to answer the SAME blank (instead of the round-robin
      one-blank-per-player split from `group-play/02`) - each of those players
      independently submits their own word for that one blank, blind to what
      the others submitted (Classic-blind-style: no player sees another
      player's Versus answer before the reveal).
- [ ] AC-02: Given the engine's collection model, then it is generalized to
      allow MULTIPLE `SubmittedWord`s recorded against a single blank id (today
      `CollectedWords` is a `Map<string, SubmittedWord>` - one winner per key;
      Versus needs a shape that holds a small ordered list per blank id for
      exactly the blanks configured as Versus, while every other blank in the
      SAME round keeps the existing one-per-blank behavior unchanged) - this
      generalization lives in `web/src/engine/engine.ts`, not in a Versus-only
      module, so it is available to any future mode that also needs
      many-answers-per-blank.
- [ ] AC-03: Given a Versus blank has multiple submitted answers, when the story
      is assembled for the reveal, then ALL of the competing answers are
      available to render (not just one arbitrarily chosen) - `assemble.ts`'s
      pure, deterministic contract is preserved (same inputs -> same output,
      never throws), and this story documents/extends its shape rather than
      forking a parallel assembly function.
- [ ] AC-04: Given the reveal for a round containing a Versus blank, then the
      room sees ALL competing answers for that blank presented together (e.g.
      each rendered inline in its own copy of the surrounding sentence, or as a
      labeled set) and a lightweight VOTE step: each player taps to pick the
      answer they find funniest for that blank; a tally increments live for
      everyone in the room (same one-SignalR-connection real-time pattern as
      the existing roster broadcast), and the winning answer is highlighted
      once the vote step is complete.
- [ ] AC-05: Given the vote step, then it uses the SAME reusable vote-collection
      primitive as `reveal-delight/03`'s Golden Guardian award (tap-to-pick-one,
      tally, surface a winner) - this story and `reveal-delight/03` do not each
      invent their own vote-counting logic; whichever of the two lands first
      builds the shared primitive in `web/src/engine/` and the other consumes
      it. Document which one actually built it once build order is known.
- [ ] AC-06: Given the Versus vote result, then it stays LIGHT and social: no
      cumulative score, no leaderboard, no "you lost" framing anywhere in the
      UI - the vote surfaces which answer got the most laughs for THIS blank in
      THIS round and nothing more (README section 1 - QuibbleStone is a toy for
      hilarity, not a competition; mirrors the tone guard already written into
      `reveal-delight/03`).
- [ ] AC-07: Given free-text Versus answers, then every submitted word passes
      the safety filter before it is recorded or shown in the vote step - same
      collection-path seam every mode inherits (no Versus-specific bypass); no
      PII is collected by the vote itself (a vote is anonymous within the room,
      same posture as every other player action).

## Out of Scope
- More than one Versus blank per round (start with exactly one "duel blank" per
  round; multiple simultaneous duels is a later enhancement once the primitive
  is proven).
- Versus combined with word-bank or progressive-reveal axes (prove it against
  Classic-blind-shaped axes first; combining is a follow-up once the
  generalization is stable).
- Solo play offering Versus (there is no second player to duel - inherently
  group-shaped, needs at least 2 players assigned to the same blank).
- Any scoring, ranking, or persistent "funniest player" tracking across rounds
  (explicitly rejected by AC-06 and by README section 1's toy-not-competition
  stance).
- Host controls to pick WHICH blank becomes the Versus blank or WHICH players
  duel (a simple fixed rule - e.g. the first blank, all present players - is
  enough to prove the mechanic; host curation is a later enhancement).

## Technical Notes
- **This is the engine-stretch story - treat it differently from 03/04/05.**
  Where those three add a `ModeConfig` value and leave `engine.ts` untouched,
  this one is EXPECTED to touch `web/src/engine/engine.ts` (and possibly
  `web/src/engine/assemble.ts`'s consuming shape, though not its core
  determinism contract). That is not scope creep - it is the explicit,
  documented generalization the feature.md Design notes call out. Keep the
  generalization minimal: today's one-per-blank behavior must be provably
  unchanged for every non-Versus blank and every non-Versus mode (regression
  cover this with `engine.test.ts`).
- A workable shape: extend collection to key by `(blankId, submissionSlot)`
  instead of `blankId` alone for blanks marked as multi-answer, OR introduce a
  small parallel `VersusCollectedWords: Map<string, SubmittedWord[]>` used only
  for blanks flagged as Versus in that round's setup, leaving the existing
  `CollectedWords` map's single-value-per-key contract untouched for everyone
  else. Prototype both against `engine.test.ts` before committing; whichever
  keeps `toOrderedWords`/`assembleStory`'s existing callers unmodified for the
  non-Versus path wins.
- The vote primitive (AC-05): design it as a small, pure, reusable module in
  `web/src/engine/` (e.g. `vote.ts`) - a `createVote(optionIds)`,
  `castVote(vote, voterId, optionId)`, `tally(vote)` shape - so it has NO
  opinion on what the options ARE (competing Versus answers vs. Golden
  Guardian's coral words) and no opinion on how results render. Both this
  story's Versus reveal step and `reveal-delight/03`'s post-reveal tap consume
  it identically. Coordinate build order with whoever picks up
  `reveal-delight/03` - see that story's Technical Notes for the same note in
  reverse.
- Real-time: the vote tally is genuinely live room state (everyone's tap should
  update everyone's count), so it needs a hub broadcast alongside the existing
  reveal-transition pattern owned by `group-play/03` (`GameHub.cs`,
  `useGameHub.ts`) - a `VoteTallyChanged`-shaped event, same pattern as
  `RosterChanged`. That wiring is out of this story's own footprint but is
  called out so the group-play story that eventually schedules Versus wires it
  through the existing one-connection hub rather than a new channel.
- Distribution of "who duels on which blank" is a variant of
  `group-play/02`'s round-robin distribution, not a replacement for it - most
  blanks in a Versus-enabled round still distribute normally; only the
  designated duel blank gets the multi-assign. Keep `distribute.ts`'s existing
  pure round-robin function untouched; add the duel-blank assignment as a
  small, separate, additive step.
- Every color/spacing token from `web/src/theme.ts`; icons from FontAwesome
  only. The reveal's multi-answer presentation and vote UI reuse the existing
  coral-word highlight treatment (`Reveal.tsx` / `revealParts.ts`) for
  consistency - a Versus answer is still a filled-in word, just one of several
  competing for the same slot.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: two players assigned to one blank each submit independently; neither sees the other's answer pre-reveal |
| AC-02 | `web/src/engine/engine.test.ts` - regression proves single-answer-per-blank behavior is unchanged for non-Versus blanks; new coverage proves multiple answers record against one Versus blank id |
| AC-03 | `web/src/engine/assemble.test.ts`-style unit test - assembly remains pure/deterministic and exposes all competing answers for a Versus blank |
| AC-04 | manual: reveal shows all competing answers together; tapping a vote increments a live tally visible to the whole room |
| AC-05 | code review + `web/src/engine/vote.test.ts` - Versus's vote step and `reveal-delight/03`'s Golden Guardian vote both call the same primitive |
| AC-06 | manual + copy review: no score, ranking, or "lost" language appears anywhere in the Versus vote UI |
| AC-07 | `web/src/engine/engine.test.ts` (existing safety-hook coverage) exercised against a Versus submission |

## Dependencies
- game-modes/01-mode-interface
- game-modes/02-classic-blind
- group-play/02-distribute-blanks (the duel-blank assignment builds on this)
- group-play/03-collect-words (the reveal-transition broadcast this extends)
- the-reveal/01-text-reveal (the reveal screen this presents multiple answers within)
- child-safety/01-profanity-filter
- reveal-delight/03-golden-guardian (shares the vote-collection primitive - coordinate build order)
