# Story: Client routing (react-router) - real URLs + deep-link join

**Feature:** Design System & UI Foundation  ·  **Status:** In Progress  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #59

## Context
Navigation today is a single `view` state switched in `web/src/App.tsx`, with the
live game screens (round / reveal / recap) driven by SignalR state that overrides
the view. There are no URLs, no browser back/forward, no refresh-to-current-screen,
and - the blocker for growth - no deep link a shared join link can point at. This
story un-parks the decided react-router adoption (design-system feature.md Parked
#59): introduce real routes, add a `/join/:code` deep link, and keep the ONE SignalR
connection mounted ABOVE the router so it is never remounted by navigation. It is a
FAITHFUL refactor: the real-time-driven flow (a RoundStarted broadcast routing every
player into the round) must behave exactly as it does today - the URL reflects state,
state stays the authority. See [feature.md](./feature.md) and
`session-engine/06-share-room-link.md` (the deep-link join this unblocks).

## Acceptance Criteria
- [ ] AC-01: Given the app, then navigation uses react-router with real routes: `/`
      (Home), `/host` (HostSetup), `/join` (Join), `/join/:code` (Join pre-filled from
      the URL), `/solo`, `/lobby`, `/round`, `/reveal`, `/recap`. The address bar
      reflects the current screen and browser back/forward work for the entry screens.
- [ ] AC-02: Given the one SignalR connection (`useGameHub`), then the hook is mounted
      ONCE ABOVE the router so navigation never remounts or duplicates the connection
      (CLAUDE.md - one shared connection). Hub/API URLs still come from `import.meta.env`.
- [ ] AC-03 (behavior-preserving, non-negotiable): Given the live game flow, then it
      behaves exactly as before - when the hub sets `round` / `reveal` (a broadcast to
      every player), each client routes into the round / shared reveal / recap in the
      same precedence as today (reveal-recap > reveal > round > lobby), with no refresh.
      State remains the authority; the router reflects it. No regression to the scary
      2-player real-time path (README section 4).
- [ ] AC-04 (deep link): Given someone opens `/join/MOSS`, then the app loads on the
      Join screen with the code pre-filled and normalized (only nickname + Guardian left
      to choose) - the concrete surface `session-engine/06` shares. An unknown/expired
      code fails as gracefully as a mistyped one.
- [ ] AC-05: Given a refresh, then the app restores to a sensible screen for the
      current (client-only) state: entry screens (`/`, `/host`, `/join`, `/solo`)
      restore as-is; an in-game URL with no live room (rooms are ephemeral, and rejoin
      is the separate resilience track) redirects home rather than showing a broken
      shell - no crash, no blank screen.
- [ ] AC-06: Given the refactor, then it is presentation/navigation only - no change to
      `useGameHub`'s hub contract, the engine, or any game logic; the child-safety and
      no-PII posture is untouched (a code in the URL is not PII, exactly as
      `session-engine/06` establishes).

## Out of Scope
- The share payload that PUTS the `/join/:code` link into a Web Share / copy action -
  that is `session-engine/06` (this story only makes the route exist and hydrate).
- Room REJOIN / reconnect after a drop (restoring roster/round state on refresh) - the
  separate resilience track; this story only ensures a stale in-game URL degrades safely.
- Route-based code-splitting / lazy loading (a later perf pass; keep the bundle simple).
- Auth-guarded routes (no accounts yet, README section 3).
- Changing any screen's visual design (pure navigation plumbing).

## Technical Notes
- Add `react-router-dom`. Mount `<BrowserRouter>` in `main.tsx` or at the top of `App`
  with `useGameHub` called ABOVE `<Routes>` (AC-02) so the connection is stable across
  navigations.
- **State-authoritative pattern (AC-03):** keep the existing precedence logic, but
  express it as a derived target path + a single `useEffect` that `navigate()`s there
  when hook state (`room`/`round`/`reveal`/recap flag) changes - so a broadcast still
  drives navigation, now via the router instead of a `view` switch. Entry-screen
  navigation (Home CTAs) becomes `navigate('/host' | '/join' | '/solo')`.
- **`/join/:code` (AC-04):** the Join screen reads the route param (`useParams`),
  normalizes/upper-cases it, and seeds its code field, running the same validation a
  typed code gets (`session-engine/02`). `/join` with no param behaves as today.
- **Refresh safety (AC-05):** on load, client state has no room, so guard the in-game
  routes: if `/lobby|/round|/reveal|/recap` render with no live `room`, redirect to `/`.
- FontAwesome/theme unchanged; TS strict; no `any`. No em dashes.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual + Playwright smoke: routes resolve; address bar reflects screen; back/forward work on entry screens |
| AC-02 | code review: `useGameHub` is above `<Routes>`; a navigation does not reconnect (one connection in logs) |
| AC-03 | manual (2 browser contexts): host starts a round; both clients route into round -> reveal -> recap exactly as before |
| AC-04 | manual: open `/join/MOSS` in a fresh tab; Join shows the code pre-filled; a bad code fails gracefully |
| AC-05 | manual: refresh on `/lobby` with no live room redirects home (no crash); entry routes restore as-is |
| AC-06 | `npm run test:unit` stays green (engine/content unchanged); code review confirms no hub-contract change |

## Dependencies
- design-system/01-mui-theme-and-app-shell (the app shell + screens being routed)
- session-engine/01-05 (the screens/flows the routes wrap; unchanged by this refactor)
