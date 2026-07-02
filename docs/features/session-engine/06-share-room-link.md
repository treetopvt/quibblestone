# Story: Share a join link to the room (deep-link share)

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** TBD

## Context
Today the Lobby share (session-engine/04) sends the bare room code as text ("Join
my QuibbleStone game! Room code: MOSS"). A recipient in a different house still has
to open the app, find the Join screen, and type the code in by hand - friction on
the exact "different houses" moment this feature exists to serve. Once client
routing lands (design-system Parked #59: react-router with a `/join/:code`
deep-link route), the share should carry a tappable LINK that drops the recipient
straight onto the pre-filled Join screen for that room. This story upgrades story
04's share payload from "a code to retype" to "a link to tap", and is the live-room
sibling of `keepsake-gallery/04` (which shares a FINISHED tale's read-only page -
this one shares an IN-PROGRESS room to join). See [feature.md](./feature.md) and
`session-engine/04-copy-share-room-code.md` (the share widget this extends).

## Acceptance Criteria
- [ ] AC-01: Given I am the host on the Lobby and client routing exposes a
      `/join/:code` route, when I tap "Share", then the Web Share payload includes a
      full, tappable deep link to this room (e.g. `https://<app>/join/MOSS`) in the
      shared text, in addition to the human-readable message - not just the raw code.
- [ ] AC-02: Given a recipient opens the shared link on any device (no app install,
      no account), then the app loads directly on the Join screen with the room code
      pre-filled from the URL, so they only choose a nickname and Guardian and tap
      "Join" - they never retype the code.
- [ ] AC-03: Given the shared link's code, when the app hydrates the Join screen
      from `/join/:code`, then the code is normalized and validated exactly as a
      hand-typed code is (same no-ambiguous-glyph alphabet, same "room not found"
      handling if it has expired) - a link to a dead room fails as gracefully as a
      mistyped code, with the same friendly message.
- [ ] AC-04: Given the "Copy" affordance from story 04, then it copies the same
      deep link (not the bare code) so a host who pastes into any app also shares a
      tappable link; the on-screen room code display itself is unchanged (still the
      big carved code from story 04 AC-05).
- [ ] AC-05: Given Web Share is unavailable (story 04 AC-04's fallback path), then
      the Copy affordance still yields the full link and no JS error is thrown - the
      link travels by whatever channel the host chooses.
- [ ] AC-06: Given the base URL for the link, then it is derived from the running
      app origin (or a `VITE_*` public-base env), never a hardcoded host (CLAUDE.md
      section 4: hub/API/app URLs come from `import.meta.env`, never hardcoded) - so
      the same code works in dev, UAT, and prod without edits.
- [ ] AC-07 (child-safety / privacy): Given the shared link, then it carries ONLY
      the room code - no nickname, no PII, no session token - and joining through it
      is the same anonymous join as any other (README sections 3 and 6). The link is
      not secret (a room code is shareable by design), so it grants nothing beyond
      "attempt to join this room", exactly like reading the code aloud does today.

## Out of Scope
- Building the router itself - this story CONSUMES the `/join/:code` route that
  design-system Parked #59 (react-router) introduces; it does not implement routing.
  If routing has not landed, this story is Blocked on it, not a place to add routing.
- QR code generation for the link (a natural follow-on, but its own story).
- Sharing a link to a FINISHED tale for read-only viewing - that is
  `keepsake-gallery/04-shareable-tale-link` (a different surface: a public read-only
  tale page, not a live room to join). This story is deliberately the live-room half.
- Deep links that pre-fill the nickname or auto-join (the recipient always picks
  their own nickname + Guardian; auto-join would smuggle identity into a URL).
- Reconnect / rejoin-by-link after a dropped connection (still the deferred
  reconnect-hardening pass, feature.md Parked).

## Technical Notes
- **Web only** (`web/src/`), no API/hub change - the code is already in client state
  from session-engine/01, and joining still uses the existing `joinRoom` hub method.
- **Depends on routing** (design-system Parked #59): the `/join/:code` route and a
  `useParams`-style read on the Join screen must exist. Keep `useGameHub` mounted
  ONCE above the router (per that parked note) so the one SignalR connection is never
  remounted by navigation.
- Build the link from `window.location.origin` (or a `VITE_PUBLIC_BASE_URL` if one is
  introduced) + the `/join/:code` path - never a literal domain (AC-06).
- This edits the same Lobby share widget as story 04 (`web/src/pages/Lobby.tsx`): the
  Web Share `text`/`url` payload and the clipboard string both become the link. Reuse
  story 04's feature-detection posture (`typeof navigator.share === 'function'`; do
  NOT gate on `navigator.canShare()` for a plain text/URL payload).
- The Join screen (`web/src/pages/Join.tsx`) gains "seed the code field from the route
  param" - normalize/upcase and run it through the same validation story 02 already
  applies to a typed code (AC-03).
- FontAwesome only for any new chrome; all styling from `web/src/theme.ts`.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: tap Share on the Lobby; confirm the payload text contains a full `/join/<code>` URL, not just the code |
| AC-02 | manual (second device/browser): open the link; confirm it lands on Join with the code pre-filled, only nickname/Guardian left to choose |
| AC-03 | manual: open a `/join/<code>` link for an expired/nonexistent room; confirm the same friendly "room not found" path as a mistyped code |
| AC-04 | manual: tap Copy; paste; confirm the clipboard holds the deep link, and the on-screen code display is unchanged |
| AC-05 | manual (Web Share unavailable): confirm Copy still yields the link and no console error |
| AC-06 | code review: the link base comes from the app origin / a `VITE_*` var, with no hardcoded host anywhere |
| AC-07 | code review: the link contains only the room code (no nickname, token, or PII); joining is the normal anonymous flow |

## Dependencies
- session-engine/04-copy-share-room-code (the share/copy widget this upgrades)
- session-engine/02-join-with-code (the Join screen + code validation the link pre-fills)
- design-system Parked #59 - client routing (react-router) with the `/join/:code`
  deep-link route (hard prerequisite - this story is Blocked until it lands)
