// ----------------------------------------------------------------------------
//  feedbackLog.ts - the web half of the anonymous per-tale thumbs up/down
//  curation vote (story-selection/05, issue #95).
//
//  WHAT THIS IS: a thin, fire-and-forget REST client for POST /telemetry/feedback
//  - the QUIET, per-player, per-round "did you like this story?" vote on a
//  TEMPLATE (a curation signal), recorded at the end of a tale via
//  ../components/TaleFeedback.tsx. This is NOT the reveal-delight Reaction row:
//  there is no SignalR, no live room tally, no aggregate shown to players
//  (contrast reveal-delight/01, which IS room state). It mirrors serveLog.ts's
//  shape exactly - a single fetch, no retry, no await on the caller's path.
//
//  FIRE-AND-FORGET / NEVER GATES GAMEPLAY (AC-05): recordFeedback returns
//  immediately (void) - the caller (TaleFeedback.tsx) does NOT await it and the
//  visible thumb selection is set from LOCAL state regardless of the network
//  result. Any failure (network, non-OK status, storage) is swallowed via
//  .catch(); there is no retry. If the sink is down, slow, or unreachable, the
//  player's tap still "sticks" on screen and nothing about game flow changes.
//
//  NO PII (AC-04, README section 6): the body carries ONLY anonymous facts -
//  the template id, the vote ("up"/"down"), the mode, an opaque per-round vote
//  id (minted by the caller, doubles as the server's upsert key so a changed
//  vote overwrites rather than double-counting), and the SAME opaque
//  per-device session GUID story-selection/04's serve log already mints and
//  persists (reused via getOrCreateSessionId - NOT re-minted here). Never a
//  nickname, never a join code, never anything traceable to a person. There is
//  no free text anywhere on this surface (nothing for the safety filter).
//
//  Config: the API base URL comes from import.meta.env.VITE_API_BASE_URL (typed
//  in web/src/vite-env.d.ts) - never hardcoded, per CLAUDE.md section 4. Secrets
//  never ship in VITE_ vars; this endpoint is anonymous and carries none.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { getOrCreateSessionId } from './serveLog';

/** A player's thumbs choice on a tale - the only two accepted values (AC-04). */
export type FeedbackVote = 'up' | 'down';

/** The anonymous per-tale feedback vote to record. */
export interface FeedbackRecord {
  /** The story template this vote is about (the curation subject, not the round instance). */
  templateId: string;
  /** The player's current thumbs choice. */
  vote: FeedbackVote;
  /** The play mode the tale was served under ("solo" or "classic-blind"). */
  mode: string;
  /**
   * An opaque, per-round GUID minted CLIENT-SIDE once per viewing of the
   * reveal/recap screen (see TaleFeedback.tsx). Reused across every re-tap
   * while the screen stays mounted, so the server upserts on it - a changed
   * vote overwrites the same stored row instead of double-counting (AC-02).
   */
  voteId: string;
}

/**
 * Fire-and-forget: records ONE anonymous thumbs up/down vote for a tale
 * (AC-01/AC-02). Returns void immediately - the caller never awaits it and it
 * never blocks or reverts the visible selection. Any failure (network, non-OK
 * status, storage) is swallowed; there is no retry (AC-05). Carries no PII and
 * no free text (AC-04).
 */
export function recordFeedback({ templateId, vote, mode, voteId }: FeedbackRecord): void {
  const body = {
    templateId,
    vote,
    mode,
    voteId,
    sessionId: getOrCreateSessionId(),
  };

  // A single fetch, no await on the caller's path, no retry. Swallow every
  // failure - telemetry must never surface to a player or wedge game flow.
  void fetch(`${import.meta.env.VITE_API_BASE_URL}/telemetry/feedback`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  }).catch(() => {
    // Best-effort telemetry: a down / slow / unreachable sink is a no-op here.
  });
}
