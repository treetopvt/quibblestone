<!--
  Implementation plan for the replay-remix feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Replay & Remix

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

Three thin extensions of an engine and a round lifecycle that already exist (README section 8 - "additive on a thing
that already works"). None of the three stories change `assemble()`, `collectWord()`, or the mode interface - they
are new **callers** of those functions, plus small additions to the **same** `GameHub.cs` / `useGameHub.ts` the
`group-play` and `session-engine` features already grew. Because 01 and 03 both touch host-authorization logic in
`GameHub.cs`'s round/room-mutation region, they serialize; 02 is engine-level plus a sibling broadcast method and can
run alongside either.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Round lifecycle + host authorization | the existing `startRound` host-only pattern (**group-play/01**) | `api/src/Hubs/GameHub.cs` |
| Round Complete screen + replay CTA | the existing screen and "Play another round" action (**group-play/04**) | `web/src/pages/RoundComplete.tsx` |
| Reveal screen | the shared Reveal view, rendered as-is (**the-reveal/01**) | `web/src/pages/Reveal.tsx` |
| Engine collect + assemble | `collectWord()` / `assembleStory()` / `toOrderedWords()`, called again, never reimplemented (**game-modes/01**) | `web/src/engine/engine.ts` |
| Deterministic assembly | `assemble()`, called again by `assembleStory()` | `web/src/engine/assemble.ts` |
| Roster + host flag | the room/player model and `RosterChanged` broadcast (**session-engine/01, /03**) | `api/src/Hubs/GameHub.cs`, `api/src/Rooms/` |
| Real-time | the one SignalR connection hook | `web/src/signalr/useGameHub.ts` |
| Avatar / host indicator | `<Guardian variant size />` + the existing crown badge styling (**design-system/02**) | `web/src/components/Guardian.tsx` |
| Styling / theme tokens | the MUI theme (palette, typography, radii, spacing) | `web/src/theme.ts` |
| Shared UI contracts | `AppBar`, `Button` (gold/outlined-purple), `BottomActionBar` | `web/src/components/` |
| Child safety | the server-side filter every re-collected word still passes | `api/src/Safety/` |

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. Overlap on a file (both 01 and
03 touch host-authorization code in `GameHub.cs`'s round-mutation region) means serialize.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 carve-it-again | TBD | edits `api/src/Hubs/GameHub.cs` (`startRound` gains a pinned-template-id param), `web/src/signalr/useGameHub.ts`, `web/src/pages/RoundComplete.tsx` (second action) | group-play/04, group-play/01 | 02 (disjoint files) | 1 | low |
| 02 one-blank-remix | TBD | `web/src/engine/remixHelpers.ts` (pure blank-picker list), edits `api/src/Hubs/GameHub.cs` (new sibling broadcast method), `useGameHub.ts`, `web/src/pages/Reveal.tsx` (secondary action + single-blank prompt step) | the-reveal/01, group-play/03 | 01 (disjoint files) | 1 | medium |
| 03 rotating-host | TBD | edits `api/src/Hubs/GameHub.cs` (new `PassHost` method + host-authorization region), `useGameHub.ts` (isHost can now flip from an incoming roster broadcast, not only from create/join), `web/src/pages/Lobby.tsx` + `RoundComplete.tsx` (handoff action on a roster tile) | 01 (recommended, not a hard technical block - see story Dependencies) | - | 2 | medium |

**Concurrency per wave:** Wave 1 = {01, 02} in parallel (disjoint files - 01 touches the round-start signature and
`RoundComplete.tsx`; 02 touches a new remix broadcast method and `Reveal.tsx`). Wave 2 = 03 alone, after 01 lands, so
the host-authorization surface it extends (and the `GameHub.cs` region 01 just edited) is stable before a second story
edits the same authorization logic and `useGameHub.ts`'s `isHost` tracking.

## Per-story tech notes

### 01 - Carve it again: same-crew, same-template replay
**Approach:** add an optional pinned-template-id argument to the existing `startRound` hub method rather than a new
method name, so "start a round" stays one wire shape whether the template is host-chosen, random, or pinned. Host
authorization is the same check `group-play/01` already performs - do not add a second authorization code path.
**Key files:** `api/src/Hubs/GameHub.cs` (the `startRound` signature and its authorization block), `web/src/pages/RoundComplete.tsx`
(a second, lower-emphasis action beside the existing "Play another round" CTA). **Exports:** nothing new for other
stories to import - this is a leaf extension of an existing method.

### 02 - One-blank remix of a finished tale
**Approach:** entirely a new **caller** of `collectWord` + `assembleStory` (`web/src/engine/engine.ts`) - hold the
prior `CollectedWords` map (or reconstruct it from `AssembledStory.filledWords` + the `Template`), overwrite the one
remixed `blankId`, re-run assembly. The one genuinely new piece is a sibling hub broadcast method (mirroring the
reveal-broadcast pattern `group-play/03` already established) so a remix reaches every player in the room, not just
the remixer. A pure helper `remixHelpers.ts` lists remixable blanks (category + current word) from an
`AssembledStory` + `Template` - this is the Vitest-testable core (AC-02's picker list, AC-04's overwrite-only-one-blank
guarantee). **Key files:** `web/src/engine/remixHelpers.ts` (new, pure), `api/src/Hubs/GameHub.cs` (new sibling
broadcast method, disjoint from 01's `startRound` edit), `web/src/pages/Reveal.tsx` (a secondary "Remix a word" action
+ a single-blank prompt step reusing FillBlank's stone-tablet input pattern). **Exports:** `remixHelpers.ts`'s
blank-picker function, in case a later feature (`keepsake-gallery`) wants to show "what got remixed."
**Gotcha:** confirm before building whether "who can remix" in group play is open to any player or host-only (noted
as an open call in the story; default assumption is open-to-any-player) - this is a one-line guard on the new hub
method, not an architecture question, but it should be settled once rather than drift.

### 03 - Rotating host ("Pass the chisel")
**Approach:** a new `PassHost` hub method following the exact shape of `CreateRoom`/`JoinRoom` (validate under the
room's lock, mutate, broadcast via the existing `RosterChanged` event - no new broadcast event type needed, since the
host flag already rides on `PlayerDto.IsHost`). Validates: caller is current host (same pattern as `startRound`'s
check), room phase is `lobby` or `roundComplete` (never mid-round), target is a live roster member. The one client-side
nuance: `useGameHub.ts`'s `isHost` is currently set only from the caller's own `createRoom`/`joinRoom` action (per the
hook's existing header comment - IsHost is not tied to a connection on the wire); this story adds a path where an
incoming `RosterChanged` can also flip the LOCAL client's `isHost` if the handoff makes them the new host or demotes
them from being the old one. **Key files:** `api/src/Hubs/GameHub.cs` (new `PassHost` method, same
host-authorization region 01 touched - hence the recommended-after-01 ordering), `web/src/signalr/useGameHub.ts`
(the `isHost`-from-broadcast nuance), `web/src/pages/Lobby.tsx` + `web/src/pages/RoundComplete.tsx` (a handoff action
on a roster tile, host-only, phase-gated). **Exports:** nothing new for other stories - a leaf capability on the
existing roster/host model.

## Cross-cutting concerns

- **One engine, many thin modes, still holds.** None of these three stories touch `assemble()`, `collectWord()`, the
  `Template`/`ModeConfig` types, or the mode interface. If building any of them ever seems to require a change to
  those signatures, that is an abstraction leak (playbook Principle 2) - stop and flag it rather than patching
  around it. Story 02 is the one that looks closest to "engine work" and it is deliberately scoped as composition
  only (call the existing functions twice), never a new engine function.
- **One SignalR connection, one hub file.** All three stories add invokes/handlers to the existing
  `useGameHub.ts`/`GameHub.cs` pair - never a second `HubConnection`, never a second hub class. The
  host-authorization check is the ONE pattern (`startRound`'s existing check) - stories 01 and 03 both reuse it
  rather than each inventing their own "am I the host" logic, which is exactly why they serialize (same code region).
- **Child safety on every re-collected word.** A remix (02) submits new free text exactly like any normal round -
  it must pass the same server-side/engine-boundary safety check as any other submission. No new filter, no bypass.
- **No PII, still anonymous.** Passing host (03) moves a boolean on an already-anonymous player record; it never
  introduces a new data field, an account, or any identity beyond the existing nickname + Guardian variant.
- **Between-rounds discipline for host handoff.** Story 03's AC-05 (no mid-round handoff) is a deliberate product
  boundary, not a technical gap - do not "helpfully" extend it to mid-round without a fresh decision logged in
  `feature.md`.
- **Inter-feature ordering (prerequisites):** `session-engine` (room + roster), `group-play` (the full round
  lifecycle: start, distribute, collect, reveal, round complete - this feature extends that chain, it does not
  precede it), `the-reveal/01` (the screen 02 extends), `child-safety/01` (the filter every story still calls). This
  feature cannot start before `group-play/04` lands, since 01 and 03 both extend behavior `group-play/04` defines
  (the Round Complete screen and its replay seam).
- **No i18n** (plain strings), **big tap targets**, **no em dashes**.
