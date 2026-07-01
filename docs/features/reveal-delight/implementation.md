<!--
  Implementation plan for the reveal-delight feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Reveal Delight

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

The payoff moment "deserves the most love" (README section 10). This look-ahead feature layers reactions, a carving-
in animation, and a light social award on top of Slice 1's already-shipped text reveal (`the-reveal/01`), without
re-architecting it. All three stories are **additive UI/interaction over `Reveal.tsx`** - none change what
`assemble()` or `buildRevealParts()` produce, only how the result is presented and reacted to. Two of the three (01
Reaction row, 03 Golden Guardian) need a genuinely new, room-wide real-time surface (`GameHub.cs` +
`useGameHub.ts`); one (02 carving animation) is purely client-local CSS/React over data every client already has.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| The Reveal screen this feature layers onto (owns story text, coral highlight, confetti, pinned actions) | `Reveal.tsx` (**the-reveal/01**) - extend, do not fork | `web/src/pages/Reveal.tsx` |
| Word-highlight interleave (source of the tappable/animatable coral words) | `buildRevealParts()` (**the-reveal/01**) | `web/src/pages/revealParts.ts` |
| Assembled story / attribution (unchanged by this feature) | `assemble()` / `AssembledStory` (**template-model/01**) | `web/src/engine/assemble.ts` |
| Real-time connection (add reaction + vote invokes/handlers) | the one SignalR hook | `web/src/signalr/useGameHub.ts` |
| API hub (add reaction + vote surfaces alongside the roster/reveal pattern) | the in-app SignalR hub | `api/src/Hubs/GameHub.cs` |
| In-memory room/round state (reaction counts, vote tallies, crowned player live here - ephemeral, no DB) | `RoomRegistry` / `Room` | `api/src/Rooms/` |
| Styling / theme tokens (gold winner ring, pill colors, no new hardcoded hex) | the MUI theme | `web/src/theme.ts` |
| Shared UI contracts | `AppBar`, gold-CTA + outlined-purple Button, `BottomActionBar` | `web/src/components/` |
| Avatar (Golden Guardian's crown overlay) | `<Guardian variant size />` (**design-system/02**) - extend with an optional `crowned` prop, not a new variant | `web/src/components/Guardian.tsx` |
| Icons | FontAwesome, registered once (add crown/laugh/heart/sparkle/star/vote icons here) | `web/src/fontawesome.ts` |
| Shared vote-collection primitive (Golden Guardian's tap-to-pick-one-winner mechanic) | `vote.ts` - **this story builds it first**; the parked Versus/Duel mode (`docs/features/game-modes/feature.md` "Parked - Phase 2+/3") imports it unmodified when eventually scheduled | `web/src/engine/vote.ts` |
| Child safety (no new free-text surface introduced by this feature; words already vetted upstream) | the single server-side filter | `api/src/Safety/` |

What this feature **exports** that others import:
- Extensions to `Reveal.tsx` (reaction row, carving-in animation, tappable/vote-highlighted coral words) - no other
  feature is expected to import these as standalone modules; they live inside the screen they enhance.
- `vote.ts` (story 03) - the shared vote-collection primitive the parked Versus/Duel mode will also consume once it
  is un-parked and scheduled (see `docs/features/game-modes/feature.md` "Parked - Phase 2+/3"). See that story's
  cross-reference.
- An optional `crowned` prop on `Guardian` (story 03) - available to any future screen that renders a player's
  avatar during the crowned round.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. Overlap on `Reveal.tsx` (all three
stories touch it, in different regions) means verify actual line-level disjointness before fanning out, or
serialize.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 reaction-row | #56 | edits `web/src/pages/Reveal.tsx` (new reaction-row region) or extracts `web/src/components/ReactionRow.tsx`; edits `api/src/Hubs/GameHub.cs` (react invoke + broadcast), `web/src/signalr/useGameHub.ts` | the-reveal/01, session-engine/03, design-system/01 | 02 (different region of `Reveal.tsx` - verify disjoint before running concurrently) | 1 | medium |
| 02 carving-reveal-animation | #57 | edits `web/src/pages/Reveal.tsx` (story-scroll `parts.map` entrance animation only) | the-reveal/01, design-system/01 | 01 (different region of `Reveal.tsx` - verify disjoint before running concurrently) | 1 | medium |
| 03 golden-guardian | #58 | edits `web/src/pages/Reveal.tsx` (tappable/highlighted coral words), new `web/src/engine/vote.ts`, edits `web/src/components/Guardian.tsx` (`crowned` prop), edits `api/src/Hubs/GameHub.cs` (vote invoke + resolve broadcast), `web/src/signalr/useGameHub.ts` | the-reveal/01, session-engine/03, design-system/02 | - (touches the same `Reveal.tsx` tap targets 02 animates; sequence after 02 or verify no race) | 2 | high |

**Concurrency per wave:** Wave 1 = {01, 02} - both touch `Reveal.tsx` but in genuinely different regions (01: a new
region above `BottomActionBar`; 02: the existing story-scroll `parts.map`) - confirm at Phase 1 that the diffs don't
overlap before running them as true parallel builders; if they do collide, serialize (02 first, since 03 depends on
its carving-complete timing anyway). Wave 2 = 03 alone: it needs 02's carving animation to have a defined "complete"
state before its tap targets become interactive (per 03's own AC-01 note: "once the story is fully shown ... or
immediately if 02 is not yet built"). It builds `vote.ts` for its own use; the parked Versus/Duel mode will import it
unmodified whenever it is eventually un-parked and scheduled (see `docs/features/game-modes/feature.md` "Parked -
Phase 2+/3") - no coordination is needed today since that mode is not currently scheduled.

## Per-story tech notes

### 01 - Reaction row
- **Approach:** four pill buttons (Laugh/Heart/Wow/Star) above the pinned action bar, tap-to-increment with a
  floating-icon pop (AC-01, AC-02). Real-time in group play over the existing one connection (AC-04); works
  identically, minus the broadcast, in solo (AC-05). No new free-text surface (AC-06).
- **Owns / exports:** the reaction-row region of `Reveal.tsx` (or an extracted `ReactionRow.tsx`), plus a new
  `ReactAsync` hub invoke + `ReactionCountsChanged` broadcast.
- **Gotchas:** entrance/float animation is `transform` only, never an `opacity` `@keyframes` step (design pack's own
  documented footgun - AC-03). Reaction counts are ephemeral, reset each new Reveal screen (no persistence). Out of
  scope: per-player de-dupe guard (Phase 4), reacting to an individual word (that is 03's different mechanic).

### 02 - Word-by-word "carving" reveal animation
- **Approach:** literal text renders instantly; each coral filled word pops in sequentially (staggered
  `animation-delay` by body-order index) via a `transform: scale` keyframe only (AC-01, AC-02). Skipped entirely
  under `prefers-reduced-motion: reduce` (AC-03); never blocks the rest of the screen (AC-04); purely a client-local
  presentation layer, no new hub message, since every client already has the identical assembled story (AC-05).
  Final rendered result is pixel-identical to today's instant reveal (AC-06).
- **Owns / exports:** the story-scroll entrance-animation logic inside `Reveal.tsx`'s existing `parts.map` block.
- **Gotchas:** the SAME opacity-vs-transform footgun as 01 - be extra careful here since a broken keyframe would make
  the WHOLE story look half-missing, not just one reaction icon. Out of scope: sound effects, animating the literal
  (non-blank) text, a dedicated skip-animation control beyond the reduced-motion bypass.

### 03 - "Golden Guardian" funniest-word award
- **Approach:** once the story is fully shown, every coral word becomes a tap-to-vote target for "funniest word this
  round" (AC-01); votes tally live over the one connection (AC-02); the winner is highlighted gold with a warm,
  singular announcement - never a ranked list, never a "loser" callout (AC-03). The winning contributor's `<Guardian
  crowned />` wears a crown for exactly the next round (AC-04), and there is **no** cumulative leaderboard, ever
  (AC-05, a permanent Decision per `feature.md`, not just an Out of Scope note). Absent entirely in solo (AC-06); no
  new free-text/PII surface (AC-07).
- **Owns / exports:** the vote-tappable coral-word rendering in `Reveal.tsx`, the `crowned` prop on `Guardian.tsx`,
  the vote hub invoke/broadcast, and the shared `vote.ts` primitive (built here first - the parked Versus/Duel mode
  imports it unmodified when it is eventually scheduled).
- **Gotchas:** the crown's "next round only" lifecycle is server-tracked round state, not a client timer. Sequence
  after 02 (or verify no race) since the vote targets are the same elements 02 animates in. Out of scope: any
  leaderboard/win-count (permanently rejected), tie-breaking drama, voting on anything but a single coral word.

## Cross-cutting concerns

- **This feature never re-architects the text reveal.** `assemble()` and `buildRevealParts()` stay untouched across
  all three stories - everything here is presentation/interaction layered on their existing, unmodified output, per
  `the-reveal/feature.md`'s own "grows later without re-architecting" design note.
- **Real-time rides the one connection.** Stories 01 and 03 each add a small invoke + broadcast pair to
  `api/src/Hubs/GameHub.cs` / `web/src/signalr/useGameHub.ts`, following the established `RosterChanged` pattern
  (register once, guard against post-leave/teardown races) - never a second `HubConnection`. Story 02 needs no hub
  change at all (purely client-local).
- **Shared vote primitive, built here for a parked mode.** Story 03 and the parked Versus/Duel mode (see
  `docs/features/game-modes/feature.md` "Parked - Phase 2+/3") both need "tap to pick one winner among options,
  tally, surface a result." Story 03 builds `web/src/engine/vote.ts` once, generally (no opinion on what the options
  represent), so Versus/Duel can import it unmodified whenever it is eventually un-parked and scheduled - do not let
  a second vote implementation drift into existence later.
- **Tone discipline: no scoring, ever.** README section 1 - QuibbleStone is a toy for hilarity, not a competition.
  Golden Guardian (03) is explicitly a single-round, cosmetic, forgettable award; the Reaction row (01) has no
  "winning" reaction at all, just counts. Neither introduces a leaderboard, a win/loss framing, or cross-round
  tracking - this is a hard Decision (`feature.md`), not a soft preference.
- **Animation discipline: `transform` only for entrances/pops, never `opacity` keyframes on a reused/list element**
  (the design pack's own documented, previously-hit gotcha) - both 01 and 02 call this out explicitly; treat it as
  non-negotiable in code review for this feature.
- **Reduced motion** is explicitly handled by 02 (AC-03) and should be considered by 01 if the reaction float is
  built close in time - keep the two consistent if built together.
- **Child safety + no PII:** this feature introduces zero new free-text entry points. Every word it displays or
  makes tappable already passed the filter upstream (`the-reveal/01` AC-04); reaction taps and votes carry no
  identity beyond "someone in this room." **No i18n** (plain strings), **big tap targets**, **no em dashes**.
- **Inter-feature ordering (prerequisites):** `the-reveal/01` (the screen this builds on), `session-engine/03` (the
  room/roster reactions and votes broadcast within), `design-system/01` + `/02` (theme, Button, Guardian). Story 03
  additionally builds the shared vote primitive that the parked Versus/Duel mode will later reuse (see
  `docs/features/game-modes/feature.md` "Parked - Phase 2+/3") - not a hard dependency today since that mode is not
  currently scheduled; whoever un-parks it should check `vote.ts` before building a second implementation.
