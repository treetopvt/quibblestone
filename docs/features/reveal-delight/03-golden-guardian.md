# Story: "Golden Guardian" funniest-word award

**Feature:** Reveal Delight  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
After a reveal, the room gets one more small, social beat: everyone taps the
single coral word they find funniest (across the WHOLE tale, any player's
word), the taps tally, and the contributor of the winning word gets their
Guardian avatar dressed with a gold crown for the next round only - a light,
temporary, purely cosmetic badge of honor. This is new (not pulled from an
existing parking note) but deliberately shaped to match QuibbleStone's tone:
README section 1 frames the whole game as built for "hilarity and easy fun,"
not competition, so this award is designed to feel like a shared laugh, never
a scoreboard. See [feature.md](./feature.md) for the feature-level framing
and its explicit Decision against any cumulative leaderboard.

This story shares its voting mechanic with
[`docs/features/game-modes/06-versus-duel.md`](../game-modes/06-versus-duel.md)
(Versus/Duel's post-reveal vote on which competing answer is funniest) - both
are "the room taps to pick one winner among a small set of options, tally,
show the result." Whichever of the two lands first should build the shared,
reusable vote-collection primitive (`web/src/engine/vote.ts`) that the other
then imports, rather than each inventing its own tally logic. Coordinate build
order before starting either.

## Acceptance Criteria
- [ ] AC-01: Given the Reveal screen after the story is fully shown (carving
      animation complete, or immediately if `reveal-delight/02` is not yet
      built / reduced-motion is on), then every coral filled-in word in the
      story becomes tappable as a "vote for funniest" target - tapping a word
      casts (or changes) my single vote for that word; I can only have one
      active vote at a time and tapping a different word moves it.
- [ ] AC-02: Given any player casts a vote, then the room sees a live tally
      update in near-real-time over the SAME one SignalR connection the roster
      and reveal already use (`web/src/signalr/useGameHub.ts`) - no second
      connection, no polling. The specific per-word counts do not need to be
      constantly visible to everyone mid-vote (a simple "N of M have voted"
      status is enough), but the FINAL winning word is announced to everyone
      once voting resolves.
- [ ] AC-03: Given voting resolves (all present players have voted, OR the host
      taps a low-pressure "Reveal the winner" affordance to close voting early
      - mirroring the Waiting screen's "no rush, but the host can move things
      along" posture already established for group play), then the single
      word with the most votes is highlighted as the winner (a gold ring/glow
      treatment on that coral word, reusing the existing gold CTA token
      `theme.palette.gold.main`) and a short, warm announcement names it (e.g.
      "the funniest word this round: <word>") without naming a "loser" or
      ranking every other word.
- [ ] AC-04: Given the winning word's contributor, then for the NEXT round
      only, their `<Guardian variant size />` avatar (wherever it renders -
      Lobby roster tile, Waiting row, Round Complete recap) is shown wearing a
      gold crown overlay; the crown is removed automatically once that next
      round ends (it is not re-awarded, re-triggered, or carried into a third
      round unless a NEW Golden Guardian vote happens again).
- [ ] AC-05: Given the Golden Guardian mechanic across any number of rounds,
      then there is NO cumulative tally, leaderboard, win count, or any UI
      surface that ranks players against each other over time - each round's
      award is entirely self-contained and forgotten once the crown is
      removed (README section 1; see `feature.md`'s Decisions log).
- [ ] AC-06: Given I am playing solo (no room), then the Golden Guardian vote
      step is simply not offered (there is no room to vote) - this mechanic is
      inherently group-shaped and does not degrade into a no-op UI on the solo
      Reveal screen; it is absent there entirely.
- [ ] AC-07: Given the vote step, then it introduces no new free-text entry
      point and collects no PII - a vote is a tap on an already-vetted,
      already-displayed coral word (every word in `assembled.filledWords`
      already passed the safety filter upstream, per `the-reveal/01` AC-04);
      there is nothing new here for the safety filter to check.

## Out of Scope
- Any cumulative leaderboard, win-count, or cross-round "funniest player"
  history (explicitly and permanently rejected, not merely deferred - see
  `feature.md` Decisions and AC-05).
- Voting on anything other than a single coral word from THIS reveal (no
  voting on the whole story, no voting across multiple rounds at once).
- A tie-breaking UI beyond a simple, undramatic rule (e.g. first word to reach
  the highest count wins, or the host's vote breaks a tie) - keep tie handling
  boringly simple; this is a toy, not a ranked contest.
- Any mechanic that singles out or embarrasses the LEAST-voted contributor -
  the design is explicitly winner-only, never a "worst word" callout.
- The Versus/Duel mode itself (`game-modes/06`) - this story only shares its
  underlying vote-collection primitive with that mode; it does not implement
  or depend on Versus gameplay.

## Technical Notes
- **Shares its vote mechanic with `game-modes/06` (Versus/Duel).** Both need:
  create a small option set, let room members cast one vote each, tally, and
  surface a winner. Design and build `web/src/engine/vote.ts` as a small, pure,
  reusable module (`createVote(optionIds)`, `castVote(vote, voterId,
  optionId)`, `tally(vote)` - see `game-modes/06`'s Technical Notes for the
  same shape described from the other side) with NO opinion on what the
  options represent (a competing Versus answer vs. a coral word here) and no
  opinion on how the result renders. **Before starting this story, check
  whether `game-modes/06` has already built `vote.ts`** - if so, import and
  reuse it rather than re-implementing; if this story lands first, build it
  generally enough that `game-modes/06` can adopt it unmodified.
- Real-time: a new lightweight hub surface, mirroring the established
  `RosterChanged` pattern in `api/src/Hubs/GameHub.cs` /
  `web/src/signalr/useGameHub.ts` - something like `CastGoldenGuardianVote
  (roomCode, blankId)` invoke plus a `GoldenGuardianResolved`-shaped broadcast
  carrying the winning `blankId`/`playerSessionId` once voting closes. Vote
  state (who voted for what) can live on the same in-memory `Room` the
  `RoomRegistry` already tracks for the round - ephemeral, discarded once the
  round moves on (README section 4, this is a toy, not a system of record).
- The crown overlay (AC-04) is a small addition to wherever `<Guardian
  variant size />` already renders (`web/src/components/Guardian.tsx`,
  consumed by Lobby/Waiting/Round Complete per `design-system/02`) - add it as
  an optional prop (e.g. `crowned?: boolean`) rendered as a small overlay
  element ON TOP of the existing Guardian SVG, not a new Guardian variant (the
  six variants in `design-system/02` are the player's chosen identity; the
  crown is a temporary state layered over whichever variant they picked -
  keep those two concepts separate). The "next round only" lifecycle is
  server-tracked round state (attach the crowned player's session id to the
  round record, clear it when the round advances), not a client-side timer.
- Winner-word highlight styling (AC-03) reuses `theme.palette.gold.main`
  (already the CTA token) as a ring/glow around the winning coral `<Box>` in
  `Reveal.tsx`'s existing `parts.map` rendering - additive `sx`, no change to
  the coral color/weight/underline treatment itself.
- Coordinate with `reveal-delight/02` (carving animation) on build order if
  both are in flight: this story's tap targets are the SAME coral `<Box>`
  elements 02 animates the entrance of - the vote step should only become
  interactive once each word has finished carving in (or immediately if 02 is
  not yet built / reduced-motion is on), so verify the two do not race if
  built concurrently.
- Every color/spacing token from `web/src/theme.ts`; icons (a crown glyph, a
  vote/checkmark affordance) are FontAwesome only, registered in
  `web/src/fontawesome.ts`.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: tapping a coral word casts a vote; tapping a different word moves it; only one active vote per player |
| AC-02 | manual (two browser contexts): a vote cast in one context is reflected in the other's tally status without a refresh |
| AC-03 | manual: after all votes are in (or the host closes voting), the single highest-voted word is highlighted gold with a warm, singular announcement |
| AC-04 | manual: the winning contributor's Guardian shows a crown overlay on the NEXT round's Lobby/Waiting/Round-Complete tiles; gone the round after |
| AC-05 | code review + manual: no leaderboard/win-count UI or persisted cross-round tally exists anywhere in the app |
| AC-06 | manual: solo Reveal screen never shows a Golden Guardian vote step |
| AC-07 | code review: no new text input introduced; the vote payload carries only an already-vetted blank id, no PII |
| shared primitive | `web/src/engine/vote.test.ts` - covers `createVote`/`castVote`/`tally` in isolation, consumed identically by this story and `game-modes/06` |

## Dependencies
- the-reveal/01-text-reveal
- session-engine/03-player-roster (the room this story votes within)
- design-system/02-guardian-component (the crown overlay)
- game-modes/06-versus-duel (shares the vote-collection primitive - coordinate build order)
