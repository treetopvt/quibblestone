// ----------------------------------------------------------------------------
//  joinLink.ts - build the tappable `/join/:code` deep link the Lobby's share
//  widget hands out (session-engine/06, see
//  docs/features/session-engine/06-share-room-link.md).
//
//  Client routing (PR #102) already exposes a `/join/:code` route that seeds
//  the Join screen's code field (Join.tsx's `initialCode`, normalized the same
//  way a typed code is). This module is the other half: a small, pure helper
//  that turns a room code into the full URL a host can copy or share, so a
//  recipient on any device lands straight on a pre-filled Join screen instead
//  of retyping a code read aloud (AC-01, AC-04).
//
//  Why the base comes from the running app origin (or an explicit override),
//  NEVER a hardcoded host (CLAUDE.md section 4, AC-06): the SAME build runs in
//  dev, UAT, and prod behind different hosts (localhost:5173, a UAT Static Web
//  App slot, the prod domain). A literal domain baked into the client would
//  produce a dead link the moment it is opened anywhere but that one host -
//  exactly the kind of "looks fine on my machine" bug config-from-env exists to
//  prevent. Callers pass the origin in explicitly (rather than this module
//  reading `window` itself) so the function stays pure and unit-testable
//  without a DOM, and so the caller can prefer an optional
//  `VITE_PUBLIC_BASE_URL` over `window.location.origin` (e.g. a CDN edge host
//  that differs from the origin script tags are served from).
//
//  The link carries ONLY the room code (AC-07): no nickname, no session token,
//  no PII - joining through it is the identical anonymous flow as typing the
//  code by hand, and the code itself is already meant to be spoken aloud, so
//  the link grants nothing beyond "attempt to join this room".
// ----------------------------------------------------------------------------

/**
 * Build the full deep link for a room code against a given origin.
 *
 * @param code - The room code, exactly as displayed (no extra normalization
 *   here - the Join screen re-normalizes/validates it on the receiving end
 *   just like a hand-typed code, AC-03).
 * @param origin - The app's base URL (e.g. `https://app.quibblestone.example`
 *   or `http://localhost:5173`). A trailing slash is stripped so the result
 *   never doubles up as `//join/...`.
 */
export function buildJoinLink(code: string, origin: string): string {
  const base = origin.endsWith('/') ? origin.slice(0, -1) : origin;
  return `${base}/join/${code}`;
}
