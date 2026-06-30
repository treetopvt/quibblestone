<!--
  Implementation plan for the group-play feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Group Play Experience

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

The differentiator (README section 1): multiple devices, one shared story, real-time sync - the scariest part, so
Slice 1 targets a **2-player** group to de-risk it early (README section 8). Group play is **the engine
(`game-modes`) plus distribution and a shared, broadcast reveal**, all over the **one** `GameHub.cs`. Every story
here grows that hub (and `useGameHub.ts`), so the feature is a **serial chain** - concurrency 1 - and it runs
**strictly after `session-engine`** (same hub file; rooms/roster must already exist).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Room + roster + room group | the room registry/model + roster broadcast (**session-engine**) | `api/src/Hubs/GameHub.cs`, `api/src/Rooms/` |
| Real-time connection | the one SignalR hook (add round invokes/handlers) | `web/src/signalr/useGameHub.ts` |
| Engine (collect + assemble) and Classic blind | the mode interface + Classic blind + FillBlank (**game-modes/01, /02**) | `web/src/engine/`, `web/src/pages/FillBlank.tsx` |
| Template + assembly + attribution | schema + `assemble()` (**template-model/01**); seed content (**template-model/02**) | `web/src/engine/` , `web/src/content/seedLibrary.ts` |
| Reveal screen | the shared Reveal view (**the-reveal/01**) | `web/src/pages/Reveal.tsx` |
| Avatar | `<Guardian variant size />` (**design-system/02**), using each player's join variant | `web/src/components/Guardian.tsx` |
| Styling / theme + Button + BottomActionBar | the MUI theme + shared contracts (**design-system/01**) | `web/src/theme.ts`, `web/src/components/` |
| Child safety | the server-side filter on every submitted word (**child-safety/01**); family-safe template list (**child-safety/02**) | `api/src/Safety/` |

What this feature **exports:** the round lifecycle on the hub (start -> distribute -> collect -> reveal -> round
complete -> replay), the Waiting and Round Complete screens, and the pure round-robin distribution function.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. The hub is the contract; the chain is
serial.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 start-round | #30 | edits `api/src/Hubs/GameHub.cs` (host-only `startRound` + template selection), `web/src/signalr/useGameHub.ts`, `web/src/pages/Lobby.tsx` (Start CTA wiring) | session-engine/03, game-modes/02, template-model/01, child-safety/02 | single-player/01 (other feature, disjoint) | 1 | medium |
| 02 distribute-blanks | #31 | `web/src/engine/distribute.ts` (pure round-robin), edits `GameHub.cs` (assign + tell each client its own prompts) | gp/01, game-modes/01, session-engine/03 | single-player/01 | 2 | medium |
| 03 collect-words | #32 | edits `GameHub.cs` (submission + filter + progress broadcast + reveal transition), `useGameHub.ts`, `web/src/pages/Waiting.tsx` | gp/02, the-reveal/01, child-safety/01, design-system/02 | single-player/01 | 3 | high |
| 04 round-complete | #33 | edits `GameHub.cs` (round number, play-again, back-to-lobby, attribution payload), `useGameHub.ts`, `web/src/pages/RoundComplete.tsx` | gp/03, the-reveal/01, gp/02 (attribution), design-system/02 | single-player/01 | 4 | medium |

**Concurrency per wave:** 1 at every wave. The chain is `01 -> 02 -> 03 -> 04`, dictated both by the round lifecycle
dependency and by the shared `GameHub.cs` + `useGameHub.ts` (and 01 shares `Lobby.tsx` with `session-engine/03`).
The pure `distribute.ts` (02) is the one file that could in principle be built ahead, but its server wiring edits the
hub, so it serializes anyway. The whole feature runs **in parallel with `single-player/01`** (disjoint files).

## Per-story tech notes

### 01 - Host starts a round
- **Approach:** a **host-only, server-enforced** `startRound` hub method that sets the round's template + mode
  (Classic blind for Slice 1) and broadcasts the transition so all players move into FillBlank together in
  near-real-time (AC-01, AC-02). The server checks the requesting connection is the host before executing (AC-03).
  The template list offered respects the **family-safe toggle** (`child-safety/02`, AC-04). The gold "Start game"
  CTA on the Lobby is **host-only**; non-host players see a passive waiting state (AC-03).
- **Owns / exports:** the `startRound` method + the Lobby Start wiring - the seam Round Complete's "Play another
  round" reuses.
- **Gotchas:** host-check is server-side, not just UI hiding. Out of scope: mode selection beyond Classic blind,
  round timers, settings dialogs, concurrent rounds.

### 02 - Distribute blanks among players
- **Approach:** **pure round-robin** distribution over (players, blanks) - deal blanks out in player order, wrapping,
  so every blank is assigned exactly once, everyone contributes, and per-player counts differ by at most one (AC-01,
  AC-04). Round-robin (not chunked) spreads each player's words across the story, which reads funnier on reveal. The
  server assigns and tells each client **only its own** prompts, by prompt only - no story context (Classic blind,
  AC-02). Works for the Slice-1 2-player target and typical templates (AC-03).
- **Owns / exports:** `web/src/engine/distribute.ts` (pure, unit-tested) + the hub assignment wiring. The assignment
  record is the source of per-player **word-count attribution** that Round Complete (04) reads.
- **Gotchas:** keep `distribute.ts` pure (prime Vitest target). Out of scope: letting players choose blanks,
  reassignment on disconnect, weighted/host-tuned splits (round-robin is the fixed Slice-1 rule).

### 03 - Collect words and ready the reveal
- **Approach:** submissions are hub messages bound to the round + the player's assigned blank; the server validates
  each through `IContentSafetyFilter` and records it (AC-01) - words are **never** shown to other players before the
  reveal (AC-01, AC-06). When a player finishes while others write, they see the calm **Waiting** interstitial (hero
  mascot juggling "W O W", "[N] of [M] quibblers done", a row of `<Guardian variant size={54} />` tiles - done at
  full opacity + teal check, still-writing dimmed to `opacity:0.55` + pulsing badge, **no countdown**) with an
  outlined-purple "Review my words" (client-side read-only) and **no gold CTA** (intentionally passive) (AC-02 to
  AC-04). When the server sees all blanks submitted, it broadcasts the reveal transition to the room group and
  everyone moves to the Reveal screen without refreshing (AC-05).
- **Owns / exports:** the submission + progress + **reveal-broadcast** hub logic (this is where the group reveal is
  sent - `the-reveal/01` only renders), the `useGameHub.ts` handlers, and the Waiting screen.
- **Gotchas:** "Review my words" needs no server round-trip (the client already holds its answers). Waiting is
  passive by design - no countdown is a product stance, not a gap. Out of scope: editing a submitted word,
  progressive reveal, a host skip/timeout control (Phase 2), Waiting animation, reconnect handling.

### 04 - Round complete and replay loop
- **Approach:** after the reveal, "Play another round" shows the **Round Complete** screen first: "ROUND N CARVED"
  badge, confetti, story-title keepsake panel with word-count + carvers-count pills, and a "Carved by your crew" row
  of `<Guardian variant size={56} />` tiles each with display name + a **per-player word-count** caption that sums
  to the template's blank total (AC-01 to AC-03). The gold "Play another round" starts a **new round in the same
  room** (no re-join - reuses `startRound` from gp/01, resets phase to prompting) (AC-04); the outlined-purple "Back
  to lobby" returns all players to the Lobby with the room code + roster preserved (AC-05).
- **Owns / exports:** the round-number + replay/back-to-lobby hub logic, the attribution payload, the Round Complete
  screen.
- **Gotchas:** per-player word count comes from the **assignment record** the hub built in gp/02 + gp/03 - the Round
  Complete payload includes that attribution. Round number increments server-side. Confetti is **CSS-only** (8
  pieces), no canvas. Every displayed name has already passed the filter (AC-06); no PII shown. Out of scope:
  image/keepsake export, a Round Complete share sheet (the Reveal's share is the place), scoring/leaderboards,
  kicking players.

## Cross-cutting concerns

- **Inter-feature ordering (prerequisites):** `session-engine` complete (room + roster - and the **same**
  `GameHub.cs`, so group-play cannot overlap it), `game-modes/02` (engine + Classic blind + FillBlank),
  `template-model/01` (+ `/02` for content), `the-reveal/01` (Reveal screen), `child-safety/01` (word filter) +
  `/02` (family-safe list), `design-system/01` + `/02` (theme, Button, Guardian). Runs in parallel with
  `single-player/01` (file-disjoint).
- **One engine, many thin modes:** group play is the engine + distribution + a broadcast reveal - it must **not**
  fork the engine or re-implement collection/assembly. The mode stays Classic blind config (playbook Principle 2).
- **One SignalR connection:** all round messaging adds invokes/handlers to `useGameHub.ts`; never a second
  `HubConnection`. The api <-> web contract is the hub method signatures kept in sync by hand (no codegen) - so each
  story's hub change lands before its consuming web wiring compiles.
- **Real-time is the scary part:** verification uses **two browser contexts** (one creates, one joins) to confirm
  roster, round, collection progress, and the **shared reveal** sync without a refresh (playbook Phase 4).
- **Child safety + no PII:** every submitted word is filtered server-side before it is recorded or shown; only
  family-safe content is offered; names shown are already filtered. **No i18n** (plain strings), **big tap targets**,
  **no em dashes**. Reconnect tolerance for the car "dead zone" is a later hardening pass, not Slice 1.
