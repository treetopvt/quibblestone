# Feature: Reveal Delight

## Summary
Polish for the payoff moment: reactions, a carving-in animation, and a light,
social "funniest word" award. Slice 1's Reveal screen (`the-reveal/01`) is
already the celebratory text reveal; this feature is everything the design pack
and README section 10 flagged as delight-tier on top of it, without
re-architecting the reveal itself. It builds directly on `the-reveal` and pulls
forward two ideas the-reveal's own feature.md had already parked
(Reaction row in Phase 4, the carving animation in Phase 3) into their own
decomposed feature, plus a new Golden Guardian award.

## README reference
README section 10 ("Reveal screen ... the payoff moment where the hilarity
lands; deserves the most love") and section 7 (Epic Map - Phase 3 "Differentiate
& Delight" / Phase 4 "Scale & Polish" - reveal enhancements are explicitly
delight-tier, built additive on top of a working slice per section 8's roadmap).
Also README section 1 (QuibbleStone is a toy for hilarity and easy fun, not a
competition - grounds the Golden Guardian award's "light, no scoring" stance).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #56 | Reaction row | Not Started |
| 02 | #57 | Word-by-word "carving" reveal animation | Not Started |
| 03 | #58 | "Golden Guardian" funniest-word award | Not Started |
| 04 | TBD | Show who submitted each word on the reveal (group play) | Not Started |

## Dependencies
- the-reveal (this feature builds on `Reveal.tsx` / `revealParts.ts` - it does
  not re-architect the text reveal, only layers UI/animation/interaction on
  top of it).
- game-modes (specifically the parked Versus/Duel mode - see
  [`docs/features/game-modes/feature.md`](../game-modes/feature.md) "Parked -
  Phase 2+/3" - for the shared vote-collection primitive that story 03 also
  consumes).
- session-engine (a live room + roster - reactions and the Golden Guardian
  vote are room-wide, real-time, and need to know who is present).
- child-safety (no free text is introduced by this feature, but the family-safe
  posture and no-PII rule still apply to every player-facing interaction).
- design-system (theme tokens, the shared Button/AppBar/Guardian components).

## Design notes
- Nothing here changes what `Reveal.tsx` renders as the core story text -
  every story in this feature is additive UI/interaction layered on the
  existing stone-tablet scroll panel, matching the design pack's own framing
  of the Reveal screen as something that "grows later" (voices, images,
  reactions) "without re-architecting the reveal of the text itself" (see
  `the-reveal/feature.md` Design notes).
- Real-time is the connective tissue for all three stories: reaction counts,
  carving progress (in a group, so everyone watches the SAME carve, not just
  their own), and Golden Guardian votes are room-wide state, so each rides the
  **one** SignalR connection (`web/src/signalr/useGameHub.ts`) the same way
  the roster and reveal broadcast already do.
- Story 01 (Reaction row) and story 03 (Golden Guardian) are both "tap a small
  option, tally, show a result" interactions. Story 03 explicitly builds its
  vote-collection mechanic (`web/src/engine/vote.ts`) so it can later be
  shared with the parked Versus/Duel mode (see
  [`docs/features/game-modes/feature.md`](../game-modes/feature.md) "Parked -
  Phase 2+/3") - see 03's Technical Notes for the coordination note. The
  Reaction row (01) is deliberately a SEPARATE, simpler mechanic
  (multi-option, tap-to-increment, no single "winner") and does not use the
  same primitive - do not conflate the two.
- Animation discipline carries over from the design pack's own documented
  gotcha (`docs/design/README.md` Implementation Gotchas): **never animate
  opacity for an entrance/reaction pop** - drive every entrance with `transform:
  scale` only. Both 01 and 02 call this out explicitly in their ACs and
  Technical Notes because it bit the design pack once already.
- Golden Guardian's "crown for next round" reward (03) is cosmetic and
  temporary (worn for exactly the next round, then gone) - it is a costume,
  not a scoreboard entry. No cumulative tracking of who has won before.
- Story 04 (word attribution) surfaces data the engine ALREADY tracks: every
  `FilledBlank` from `assemble()` carries a `playerSessionId`, and
  `buildRevealParts()` already forwards it onto each coral `RevealWordPart` - it
  is simply never displayed. Story 04 maps that id to the roster's nickname +
  Guardian and shows "who wrote that word" as a light, tap-to-reveal delight. It
  is pure client-side presentation (no engine change, no new hub message), and it
  LABELS words only - it never scores them (that separation from the Golden
  Guardian vote is deliberate; see Decisions).

## Parked - Phase 3
- Character-voice TTS narration wired into the already-reserved narration bar
  (the-reveal's own Phase 3 parking note - unaffected by this feature).
- Saving/sharing the finished tale as an image of the tablet (the-reveal's own
  Phase 3 parking note - unaffected by this feature).

## Parked - Phase 4
- A local "already reacted" de-dupe guard per player per reaction type (the
  design pack's State Management notes this as a design question; Slice-1-of-
  this-phase ships simple tap-to-increment with no guard).
- Per-reaction floating-icon variety beyond the 4 named reactions (Laugh /
  Heart / Wow / Star) - more reaction types are a demand-driven Phase 4 idea,
  not designed speculatively here.
- Any cumulative "funniest player" leaderboard or cross-round Golden Guardian
  history - explicitly rejected by design (README section 1), not merely
  deferred; see story 03's Out of Scope.

## Decisions
- 2026-07-01: Created as a new, decomposed feature (look-ahead pass ahead of
  Slice 1 shipping) by pulling the Reaction row and carving animation out of
  `the-reveal/feature.md`'s Phase 3/4 parking notes and adding a new Golden
  Guardian award story. All three stories are Status "Not Started", Issue
  "TBD" - planned, not scheduled; they park behind `the-reveal/01` and
  `session-engine` shipping (Slice 1), and story 03 additionally builds the
  shared vote primitive that the parked Versus/Duel mode will later import
  (see `docs/features/game-modes/feature.md` "Parked - Phase 2+/3").
- 2026-07-01: Golden Guardian (03) deliberately excludes any cumulative
  scoring or leaderboard, per README section 1's "toy, not a system of record"
  / "hilarity, not competition" stance - kept as an explicit Decision (not just
  an Out of Scope note) so a future contributor does not "improve" it into a
  leaderboard without revisiting this call.
- 2026-07-02: Added story 04 (word attribution) after play surfaced that the
  "wait, YOU wrote that?!" beat was missing from the group reveal. Scoped as
  presentation-only over the `playerSessionId` the engine already tracks (no
  change to `assemble()`/`buildRevealParts()`, no new hub message) - the same
  additive-over-`Reveal.tsx` discipline as 01-03. Kept deliberately DISTINCT
  from Golden Guardian (03): attribution LABELS who wrote a word; the vote
  SCORES which word is funniest. They share the coral-word elements and must
  coordinate build order, but attribution introduces no tally, ranking, or
  leaderboard (README section 1) - do not merge the two concepts.
