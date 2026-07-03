// ----------------------------------------------------------------------------
//  jumbleClient.ts - the web half of the AI "Fresh Runes" jumble
//  (game-modes/07 AC-03, backed by ai-on-demand-generation/05).
//
//  This is a thin REST client, not a generator: the REAL AI word generation
//  lives server-side behind the AI cost gate (api/src/Ai/Jumble/
//  JumbleWordGenerator.cs), exposed over REST by
//  api/src/Controllers/AiJumbleController.cs. The browser NEVER calls AI - it
//  POSTs a category + avoid-list + the round's family-safe flag + an anonymous
//  session handle here, and gets back a MODERATED word set (already vetted
//  server-side). This module never reimplements generation or moderation.
//
//  It shapes a `RequestAiJumble` (the fetcher WordBankAnswer's Fresh Runes
//  button injects): `(category, avoid) => Promise<AiJumbleOutcome | null>`. The
//  parent (Solo / GroupRound) closes over the family-safe toggle and the
//  anonymous session handle, so the surface stays transport- and
//  identity-agnostic.
//
//  FAIL-SOFT (never break the round): any network error, non-OK status, or
//  unparseable body resolves to `null`, which the surface treats as "the gate
//  fell back" - it degrades to the FREE deterministic reshuffle (game-modes/07
//  AC-02). AI is a nicety layered on a fallback that always works; a broken
//  fetch must never throw into gameplay or block a jumble. (This is the
//  degrade-not-fail-closed posture, deliberately the OPPOSITE of
//  safety/checkWord.ts, which fails CLOSED because it gates unvetted free text -
//  here the words are already moderated server-side and the fallback is safe.)
//
//  THE ANONYMOUS SESSION HANDLE (README section 6, no PII):
//    - GROUP play: pass the join `roomCode`; the server resolves the live room's
//      anonymous Room.InstanceId and keys the gate's quota + attribution on it.
//    - SOLO play: pass `sessionId` (the device-local telemetry session id); the
//      server keys on that (solo has no room). Never a nickname / account / PII.
//
//  Config: the API base URL comes from `import.meta.env.VITE_API_BASE_URL`
//  (typed in web/src/vite-env.d.ts) - never hardcoded, per CLAUDE.md section 4.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import type { BlankCategory } from '../engine/template';
import type { AiJumbleOutcome, RequestAiJumble } from '../pages/fillblank/WordBankAnswer';

/** The anonymous session context a jumble request needs (one of roomCode / sessionId is set). */
export interface JumbleSession {
  /** The round's family-safe toggle - tightens generation + moderation server-side. */
  familySafe: boolean;
  /**
   * The template's curated theme tags (e.g. ['fantasy', 'monsters']) - a soft flavor
   * steer so AI words fit the story's vibe. The server never sees the story text, only
   * these tags, so the reveal stays unspoiled. Optional; omitted -> generic words.
   */
  themes?: readonly string[];
  /** The group join code (resolved to the anonymous Room.InstanceId server-side). Group play only. */
  roomCode?: string;
  /** The solo client's anonymous device-local session id. Solo play only. */
  sessionId?: string;
}

/** The shape returned by POST /api/ai/jumble on the API. */
interface JumbleResponse {
  words: string[];
  remainingQuota: number;
  fellBack: boolean;
}

/** Narrows an unknown parsed JSON body into a JumbleResponse, or null if it does not match. */
function asJumbleResponse(value: unknown): JumbleResponse | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (!Array.isArray(record.words) || !record.words.every((w) => typeof w === 'string')) return null;
  if (typeof record.remainingQuota !== 'number') return null;
  if (typeof record.fellBack !== 'boolean') return null;
  return {
    words: record.words as string[],
    remainingQuota: record.remainingQuota,
    fellBack: record.fellBack,
  };
}

/**
 * Builds a `RequestAiJumble` fetcher bound to one session. Calls
 * POST /api/ai/jumble; resolves to the moderated outcome, or `null` on any
 * failure (the surface then reshuffles for free). Never throws.
 */
export function createAiJumbleRequester(session: JumbleSession): RequestAiJumble {
  return async (category: BlankCategory, avoid: readonly string[]): Promise<AiJumbleOutcome | null> => {
    try {
      const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/ai/jumble`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          category,
          familySafe: session.familySafe,
          avoid: [...avoid],
          themes: session.themes ? [...session.themes] : null,
          roomCode: session.roomCode ?? null,
          sessionId: session.sessionId ?? null,
        }),
      });

      if (!response.ok) {
        return null;
      }

      const body: unknown = await response.json();
      const parsed = asJumbleResponse(body);
      if (!parsed) {
        return null;
      }

      return {
        words: parsed.words,
        remainingQuota: parsed.remainingQuota,
        fellBack: parsed.fellBack,
      };
    } catch {
      // Network failure, JSON parse failure, or any other rejection: degrade to
      // the free deterministic reshuffle rather than break the round.
      return null;
    }
  };
}
