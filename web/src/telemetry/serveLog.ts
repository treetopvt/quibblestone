// ----------------------------------------------------------------------------
//  serveLog.ts - the web half of the anonymous serve log for SOLO rounds
//  (story-selection/04, the anonymous serve log, AC-02).
//
//  A GROUP round records its "template served" event server-side, straight from
//  the hub. A SOLO round is a single browser tab with no SignalR round-trip, so
//  it fire-and-forgets a tiny POST to /telemetry/serve here on each round start,
//  exactly the way single-player reaches the safety filter through checkWord.ts.
//  This module is a thin REST client, not a filter: the server validates the
//  template id against its catalog and drops junk silently (AC-02).
//
//  FIRE-AND-FORGET / NEVER BLOCKS GAMEPLAY (AC-03): recordSoloServe returns
//  immediately (void) - the caller does NOT await it and it NEVER blocks the
//  transition into the fill screen. There is NO retry loop that could wedge the
//  solo flow: a single fetch whose failure is swallowed by .catch(). If the sink
//  is down, slow, or the API is unreachable, the solo round is completely
//  unaffected - the exact opposite posture to checkWord.ts (which fails CLOSED
//  for child safety). Telemetry is best-effort and disposable; it must never
//  cost a player their turn.
//
//  NO PII (AC-04, README section 6): the body carries ONLY anonymous facts -
//  the template id, mode "solo", the derived length class, the family-safe flag,
//  and an OPAQUE per-device session GUID. Never a nickname, never a join code
//  (solo has none), never anything traceable to a person. The session GUID is
//  minted once with crypto.randomUUID() and reused from localStorage thereafter
//  (the same device-local, no-account posture identity.ts already uses) - it is
//  NOT an account and identifies a device-local session, never a human.
//
//  Config: the API base URL comes from import.meta.env.VITE_API_BASE_URL (typed
//  in web/src/vite-env.d.ts) - never hardcoded, per CLAUDE.md section 4. Secrets
//  never ship in VITE_ vars; this endpoint is anonymous and carries none.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { classifyLength } from '../content/length';
import type { Template } from '../engine/template';

/** localStorage key for the opaque, device-local serve-log session id. */
const SESSION_ID_STORAGE_KEY = 'quibblestone.telemetry.sessionId.v1';

/**
 * Returns the opaque per-device session GUID, minting one on first use and
 * reusing it thereafter (device-local only, never an account, no PII - AC-04).
 * Never throws: if storage is unavailable or crypto.randomUUID is missing, it
 * falls back to a transient id so a serve event can still be sent anonymously
 * without ever breaking the solo flow.
 *
 * Exported (story-selection/05) so the per-tale feedback vote
 * (telemetry/feedbackLog.ts) reuses this SAME device-session GUID rather than
 * minting a second, competing device id.
 */
export function getOrCreateSessionId(): string {
  try {
    const existing = window.localStorage.getItem(SESSION_ID_STORAGE_KEY);
    if (existing !== null && existing.length > 0) {
      return existing;
    }
    const minted = crypto.randomUUID();
    window.localStorage.setItem(SESSION_ID_STORAGE_KEY, minted);
    return minted;
  } catch {
    // Storage disabled / quota / crypto unavailable: fall back to a transient,
    // still-anonymous id. Telemetry is best-effort - never break gameplay for it.
    try {
      return crypto.randomUUID();
    } catch {
      return 'anonymous-session';
    }
  }
}

/** The anonymous solo serve to record - the caller passes the chosen template + toggle. */
export interface SoloServe {
  /** The served template (its id + blank count are read; no prose is sent). */
  template: Template;
  /** The family-safe toggle position the solo round is being played under. */
  familySafe: boolean;
}

/**
 * Fire-and-forget: records ONE anonymous "template served" event for a solo
 * round (AC-02). Returns void immediately - the caller never awaits it and it
 * never blocks the round. Any failure (network, non-OK status, storage) is
 * swallowed; there is no retry (AC-03). Carries no PII (AC-04).
 */
export function recordSoloServe({ template, familySafe }: SoloServe): void {
  // Derive the length class the SAME way the content pipeline does (story-01),
  // so the log's length class matches what was actually served.
  const body = {
    templateId: template.id,
    mode: 'solo',
    lengthClass: classifyLength(template),
    familySafe,
    sessionId: getOrCreateSessionId(),
  };

  // A single fetch, no await on the caller's path, no retry. Swallow every
  // failure - telemetry must never surface to a player or wedge the solo flow.
  void fetch(`${import.meta.env.VITE_API_BASE_URL}/telemetry/serve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  }).catch(() => {
    // Best-effort telemetry: a down / slow / unreachable sink is a no-op here.
  });
}
