# Story: Web - resume the live screen, don't bounce Home

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #144

## Context
Today, a live-game URL opened with no room in state redirects Home (`App.tsx`'s
refresh-safety guard, added for the ordinary "rooms are ephemeral" case) - exactly
the wrong behavior for a transient drop or a reload mid-round, where the player
should land back on the SAME screen with their own progress intact, not get
dumped to the start. This story closes that gap and gives the roster a calm,
visible "someone's reconnecting" state instead of a seat silently vanishing for
the grace window. This is the story that finally delivers the user-visible payoff
of 07-09 - README section 8's flagship "my family is laughing in the car" moment
surviving a dropped phone. See [feature.md](./feature.md) and
`09-web-remember-and-rejoin.md` (the trigger this story reacts to).

## Acceptance Criteria
- [ ] AC-01: Given a stored reconnect handle exists and a rejoin attempt is in
      flight (story 09), then the live-route guards (`/lobby`, `/round`,
      `/reveal`) do NOT redirect Home while that attempt is pending - they show a
      brief, friendly "reconnecting your game..." beat instead of flashing Home
      first.
- [ ] AC-02: Given the rejoin succeeds, then the player lands on the SAME live
      screen the room/round/reveal state implies (reusing the existing routing
      effect's precedence: recap > reveal > round > lobby) with no further action -
      their own outstanding blanks (if any) are exactly what `Rejoin` returned,
      never a stale or duplicate prompt.
- [ ] AC-03: Given the rejoin fails (no stored handle, or story 09's failure path
      already cleared it), then the existing refresh-safety redirect to Home still
      applies - this story narrows WHEN that redirect fires, it does not remove
      it.
- [ ] AC-04: Given another player's seat is within its grace window (disconnected
      but held, per stories 07/08's `Connected` flag on the roster), then their
      roster tile visibly differs from a normal "READY" tile (a dimmed/pulsing
      "reconnecting..." treatment) so the room understands why the round is
      paused on them, rather than the tile just vanishing and silently
      reappearing.
- [ ] AC-05: Given a player deliberately leaves or returns Home, then no stale
      "reconnecting" affordance lingers and no further auto-rejoin is attempted
      for that seat (covered mechanically by story 09's clear-on-leave; this
      story confirms the UI never shows a phantom "reconnecting" tile after a
      real departure).
- [ ] AC-06 (family-friendly UX, README section 10): the "reconnecting..."
      messaging is calm and reassuring (kid-readable, no technical jargon, no
      alarming language) - a brief blip should read as "hang tight," not as an
      error state.

## Out of Scope
- Any change to the actual reconnect/rejoin MECHANICS (grace window, token, hub
  method) - those are stories 07/08/09; this story is presentation + routing
  only.
- Host migration UI (there is still no "promote a new host" surface if the
  host's grace expires) - parked, see feature.md.
- A persistent "connection quality" indicator beyond the roster tile treatment.

## Technical Notes
- `web/src/App.tsx`: the refresh-safety guards on `/lobby`, `/round`, `/reveal`
  currently redirect to `/` whenever their backing state (`room`, `round`/`room`,
  `reveal`/`room`) is null - thread a "rejoin in flight" signal (exposed by story
  09's hook) through so the guard shows a brief loading state instead of
  redirecting while that flag is true, falling through to today's `<Navigate
  to="/" />` once it resolves negatively (AC-03).
- `web/src/pages/Lobby.tsx`: `PlayerTile` (and the `Player` roster type in
  `useGameHub.ts`) gain the `connected` flag mirrored from `PlayerDto.Connected`
  (story 07); render a dimmed/pulsing variant reusing the file's OWN established
  visual idioms (`EmptySlot`'s dashed-pulse, the host's pulsing gold ring) rather
  than inventing a new visual system.
- Keep the messaging calm and generic (AC-06) - no "player X disconnected" alarm
  copy; something like "reconnecting..." under the dimmed tile is enough.
- Verify with the two-browser-context walkthrough this repo's orchestration
  playbook already uses for real-time stories (Phase 4): drop one browser's
  network mid-round, confirm the OTHER browser shows the tile change and the
  round waits, then restore the network and confirm the dropped browser resumes
  exactly where it left off.

## Tests
| AC | Test |
|---|---|
| AC-01, AC-02, AC-03 | Playwright (two browser contexts, mirroring the pattern already used for other real-time stories): create a room with two players, start a round, take one context offline mid-collection, confirm the other still shows a live (non-aborted) round with a "reconnecting" tile, bring the first back online within the grace window, confirm both land back in the same round with the reconnecting player's remaining blanks (not previously-submitted ones) and no Home redirect |
| AC-04 | Playwright / manual: the disconnected player's roster tile visibly differs (dimmed/pulsing) while its seat is held |
| AC-05 | manual: leave a room deliberately, confirm no "reconnecting" tile or auto-rejoin lingers |
| AC-06 | code review: the copy is calm, plain-language, no jargon |
| manual | reload the tab mid-round; confirm it resumes on `/round` rather than bouncing to `/` |

## Dependencies
- session-engine/09-web-remember-and-rejoin
