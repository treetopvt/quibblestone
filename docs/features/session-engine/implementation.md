<!--
  Implementation plan for the session-engine feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Session & Room Engine

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This feature grows the real-time backbone: the **one** `GameHub.cs` gains room methods and the **one**
`useGameHub.ts` gains the matching invokes/handlers. Stories 01-05 each edit those two shared files (and 03/04/06
share `Lobby.tsx`), so that core is a **serial chain** - concurrency 1 throughout. Story 06 is a later, web-only
add-on (no hub change) gated on client routing landing, so it sits outside the chain. It reuses three foundations:
`design-system` (theme, AppBar, Button, Guardian), `child-safety/01` (nickname filter), and the deployable backbone
from `platform-devops`.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Real-time connection (add invokes/handlers, never a 2nd connection) | the one SignalR hook | `web/src/signalr/useGameHub.ts` |
| API hub (grows room methods + an in-memory room registry) | the in-app SignalR hub | `api/src/Hubs/GameHub.cs` |
| Styling / theme tokens | the MUI theme (**design-system/01**) | `web/src/theme.ts` |
| Shared UI contracts | `AppBar`, gold-CTA + outlined-purple Button, `BottomActionBar` (**design-system/01**) | `web/src/components/` |
| Avatar | `<Guardian variant size />` (**design-system/02**) | `web/src/components/Guardian.tsx` |
| Child safety (nicknames are free text) | the server-side filter `IContentSafetyFilter` (**child-safety/01**) | `api/src/Safety/` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Config (hub/API URLs) | `import.meta.env` (`VITE_*`) | `web/.env.development` |

What this feature **exports** that others import:
- The **room model + registry** (ephemeral, in-memory) and the hub's room methods (`createRoom`, `joinRoom`, roster
  broadcast) - `group-play` builds its round methods on these and reuses the room group.
- The **player record** (`nickname`, `variant`, `sessionId`, host flag) carried in roster broadcasts - consumed by
  `group-play` distribution/attribution and the avatar rows on Waiting / Round Complete.
- The Home, Join, and Lobby **screens**.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. The API/hub -> consuming-web chain is
serial (the hub signature is the contract; there is no codegen step).

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 create-room | #20 | `api/src/Hubs/GameHub.cs` (room methods), `api/src/Rooms/RoomRegistry.cs`, `api/src/Rooms/Room.cs`, `web/src/signalr/useGameHub.ts`, `web/src/pages/Home.tsx` | platform-devops (backbone), design-system/01 | game-modes chain, the-reveal/01 (other features, disjoint) | 1 | high |
| 02 join-with-code | #21 | edits `GameHub.cs` (join method + code validation), `useGameHub.ts` (join invoke + roster handler), `web/src/pages/Join.tsx` (code + name form) | se/01, child-safety/01, design-system/01 | game-modes chain | 2 | high |
| 05 guardian-avatar-selection | #24 | edits `GameHub.cs` (player.`variant`), `useGameHub.ts`, `web/src/pages/Join.tsx` (avatar grid) | se/02, design-system/02, child-safety/01 | game-modes chain | 3 | low |
| 03 player-roster | #22 | edits `GameHub.cs` (roster broadcast + leave detection), `useGameHub.ts`, `web/src/pages/Lobby.tsx` | se/01, se/02, se/05, design-system/02 | game-modes chain | 4 | medium |
| 04 copy-share-room-code | #23 | `web/src/pages/Lobby.tsx` (share widget); edits `web/src/theme.ts` (filled-purple Share variant) | se/01, se/03, design-system/01 | game-modes chain | 5 | low |
| 06 share-room-link | TBD | edits `web/src/pages/Lobby.tsx` (share/copy payload -> deep link), `web/src/pages/Join.tsx` (seed code from `/join/:code` route param) | se/02, se/04, **design-system Parked #59 (client routing)** | - (Blocked until routing lands) | post-routing | low |

**Concurrency per wave:** 1 at every wave. The chain is `01 -> 02 -> 05 -> 03 -> 04`. Reason: 01/02/05/03 all edit
`GameHub.cs` + `useGameHub.ts` (the contract grows story by story); 05 and 02 share `Join.tsx`; 03 and 04 share
`Lobby.tsx`. Note the ordering choice **05 before 03**: the roster tiles render each player's chosen Guardian
variant, so the variant must be in the player record (05) before the roster (03) shows it - story 02 defaults the
variant to `teal` until 05 lands, which keeps 02 shippable on its own. The whole feature runs **in parallel with the
`game-modes` and `the-reveal` chains** (those touch the pure engine + FillBlank/Reveal screens, never the hub).

## Per-story tech notes

### 01 - Create a room and get a join code
- **Approach:** add room creation to `GameHub.cs` plus an ephemeral in-memory `RoomRegistry` (rooms expire when idle
  - AC-05; this is a toy, not a system of record, README section 4). Server-side code generation: 4 characters,
  **exclude ambiguous glyphs** O/0/I/1/l, unique among active rooms (AC-02, AC-03). Build the Home screen (kicker
  chip, stone-tablet hero with the `HeroGuardian` asset, gold "Create a game" CTA + outlined-purple "Join a game"
  + the "No account needed" reassurance line) using the shared Button components and the one connection hook.
- **Owns / exports:** the room registry + model, the host-lands-in-lobby flow, the Home screen. `group-play` reuses
  the registry and room group.
- **Gotchas:** ephemeral state only (no DB - the in-process hub holds it, CLAUDE.md section 10). Out of scope:
  reconnect/rejoin, custom/locked rooms, persisting history, Home animations.

### 02 - Join a room with a code and nickname
- **Approach:** a host-validated `joinRoom(code, displayName, variant)` hub method that checks the code, runs the
  **nickname through `IContentSafetyFilter`** before storing/showing it (AC-03), enforces in-room name uniqueness
  (AC-06), and broadcasts the roster change (AC-05). The Join screen is the two-card form (room-code card + character
  card) with the interpolated "Join [CODE] ->" gold CTA and the "100% anonymous - no email, no account" reassurance
  (AC-02). Ask only for code + name - **no PII** (AC-02).
- **Owns / exports:** the join hub method and the Join form/flow. The variant arg defaults to `teal` until story 05.
- **Gotchas:** never render an unfiltered name to anyone. Friendly, brief validation messages (the audience includes
  kids). Out of scope: rejoin-after-disconnect, persistent identity, the avatar-grid UI (that is story 05).

### 05 - Guardian avatar selection at join
- **Approach:** "the same join call, plus a chosen variant." Add `selectedVariant` state (default `teal`) and a
  3-column grid of `<Guardian variant size={52} />` tiles to the Join screen (single-select, gold ring + check-badge
  pop on selection), and extend the player model with `variant` (`purple | gold | coral | teal | sand | plum`)
  stored in room state and broadcast in roster updates (AC-03, AC-04).
- **Owns / exports:** the variant on the player record - consumed by the roster (03), Waiting (`group-play/03`), and
  Round Complete (`group-play/04`).
- **Gotchas:** entrance ring/badge uses `transform: scale` only (never opacity on a list item). The nickname filter
  (AC-05) is the same `child-safety/01` call story 02 already wires - this story just adds the variant to that call.
  Out of scope: persisting the choice across sessions (Phase 2), custom avatars, >6 variants.

### 03 - See a live player roster
- **Approach:** the hub broadcasts join/leave to the room group; the web client subscribes via the one hook and
  re-renders a 3-column tile grid (74px circle tiles, each with `<Guardian variant size={52} />`, name, role chip;
  host gets crown badge + pulsing gold ring). "Carvers gathered" count chip, scale-pop entrance + transient "[Name]
  pulled up a stone" toast, dashed pulsing empty slots, host-only "Start game" CTA (AC-01 to AC-05). Every displayed
  name has already passed the filter at join (AC-06).
- **Owns / exports:** the Lobby screen and roster rendering. The host-only Start CTA is the seam `group-play/01`
  hangs the round-start on.
- **Gotchas:** leave detection rides the SignalR connection lifecycle for Slice 1. `transform: scale` entrances
  only; the toast is short-lived state, not permanent UI. Out of scope: rich presence, reconnect states, host
  kick/mute, capacity beyond 6.

### 04 - Copy and share the room code from the Lobby
- **Approach:** **web only**, no API change (the code is already in client state from story 01). A stone-tablet share
  widget with `navigator.clipboard.writeText()` "Copy" (teal-check "Copied!" for ~1.8s, then revert) and a Web Share
  button gated on `navigator.share && navigator.canShare()` with graceful fallback when unavailable (AC-02 to AC-04).
- **Owns / exports:** the Lobby share widget. Adds a **filled-purple** Share button variant to `theme.ts` if not
  already present (a third button style per the design spec).
- **Gotchas:** "Copied!" is local component state (`setTimeout` revert), no server round-trip. This is the one
  session-engine story that edits `theme.ts` - it must land after `design-system/01` and not overlap any other
  `theme.ts` editor. Out of scope: link-with-code URLs, QR codes, deep links (those are story 06, gated on routing).

### 06 - Share a join link to the room (deep-link share)
- **Approach:** **web only**, no hub change. Upgrade story 04's Web Share + Copy payload from the bare code to a full
  `/join/:code` deep link built from the app origin (or a `VITE_PUBLIC_BASE_URL`), and seed the Join screen's code
  field from the route param so a recipient lands pre-filled and only picks a nickname + Guardian (AC-01, AC-02, AC-04,
  AC-06). Normalize/validate the route-supplied code exactly as a typed one (AC-03). This is the live-room sibling of
  `keepsake-gallery/04` (which shares a finished tale's read-only page) - deliberately a different surface.
- **Owns / exports:** the deep-link share/copy payload on the Lobby, plus "seed code from route" on Join.
- **Gotchas:** **hard dependency on client routing** (design-system Parked #59 - react-router with `/join/:code`);
  this story is Blocked until that lands and does NOT implement routing itself. Keep `useGameHub` mounted once ABOVE
  the router so the one connection is never remounted. Never hardcode the link host (AC-06). The link carries only the
  room code - no nickname, token, or PII (AC-07). Reuse story 04's `typeof navigator.share === 'function'` detection;
  do not gate a text/URL share on `navigator.canShare()`. Out of scope: QR codes, auto-join/nickname-prefill links.

## Cross-cutting concerns

- **Inter-feature ordering (prerequisites, assumed merged before this feature orchestrates):** `child-safety/01`
  (nickname filter), `design-system/01` (theme + AppBar + Button), `design-system/02` (Guardian), and the
  `platform-devops` backbone. This feature must complete before `group-play` (which grows the **same** `GameHub.cs`
  with round methods - they cannot overlap) and before `single-player/01` (which edits `Home.tsx`, owned by se/01).
- **One SignalR connection:** all real-time work adds invokes/handlers to `useGameHub.ts` - never a second
  `HubConnection`. Hub/API URLs come from `import.meta.env`.
- **Child safety + no PII:** nicknames route through the filter before display; the only data held on a player is the
  in-session nickname + Guardian variant (anonymous, README sections 3 and 6).
- **No i18n** (plain strings), **big tap targets**, **no em dashes**.
- **Reconnect tolerance is deliberately deferred** (the car "dead zone" case is a later hardening pass) - keep the
  seams clean so it can be added without a rewrite.
