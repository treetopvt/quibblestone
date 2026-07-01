// ----------------------------------------------------------------------------
//  checkWord.ts - the web half of child safety for free-text word submissions
//  (game-modes/02, README section 6 / CLAUDE.md section 5).
//
//  This is a thin REST client, not a filter: the REAL profanity/safety check
//  lives server-side (api/src/Safety/IContentSafetyFilter.cs, the child-safety
//  feature's authoritative gate) and is exposed over REST by
//  api/src/Controllers/ModerationController.cs (this same story). This module
//  never reimplements matching logic - it only calls that endpoint and shapes
//  the response to match engine.ts's injectable `SafetyCheck` seam:
//
//      type SafetyCheck = (word: string) => Promise<{ ok: boolean; message?: string }>
//
//  `checkWord` has exactly that signature, so single-player (which has no
//  SignalR round-trip for word submission) can pass it straight into
//  `collectWord` (engine.ts) as the safety hook. Group play will eventually
//  pass a hub-backed equivalent instead - FillBlank and the engine never know
//  the difference (see FillBlank.tsx's header for the reuse contract this
//  enables).
//
//  FAIL-CLOSED (non-negotiable for child safety): if the network call fails,
//  the response cannot be parsed, or the HTTP status is not OK, this returns
//  `{ ok: false, ... }` with a friendly retry message - NEVER `{ ok: true }`.
//  An unreachable safety endpoint must never let an unchecked word through to
//  a reveal or another player's screen.
//
//  Config: the API base URL comes from `import.meta.env.VITE_API_BASE_URL`
//  (typed in web/src/vite-env.d.ts, defaulted in web/.env.development) - never
//  hardcoded, per CLAUDE.md section 4.
//
//  Who imports this: single-player (game-modes/02's thin vertical slice),
//  wiring it as the `safetyCheck` argument to engine.ts's `collectWord`.
//  FillBlank.tsx itself must NOT import this directly (see that file's
//  header) - it stays transport-agnostic and only calls whatever
//  `onSubmitWord` callback its parent injects.
// ----------------------------------------------------------------------------

/** The shape engine.ts's `SafetyCheck` expects: matches it exactly. */
export interface CheckWordResult {
  ok: boolean;
  message?: string;
}

/** Friendly, fail-closed message shown when the safety endpoint cannot be reached or parsed. */
const UNAVAILABLE_MESSAGE = 'Hmm, we could not check that word just now - please try again.';

/** The shape returned by POST /moderation/check on the API. */
interface ModerationCheckResponse {
  allowed: boolean;
  message: string | null;
}

/** Narrows an unknown parsed JSON body into a ModerationCheckResponse, or null if it does not match. */
function asModerationCheckResponse(value: unknown): ModerationCheckResponse | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.allowed !== 'boolean') return null;
  if (record.message !== null && typeof record.message !== 'string') return null;
  return { allowed: record.allowed, message: record.message as string | null };
}

/**
 * Checks a candidate word against the server's authoritative safety filter
 * (POST /moderation/check). Matches engine.ts's `SafetyCheck` signature so it
 * can be passed directly as the safety hook to `collectWord`.
 *
 * Fails closed: any network error, non-OK status, or unparseable body returns
 * `{ ok: false, message: UNAVAILABLE_MESSAGE }` rather than letting the word
 * through unchecked.
 */
export async function checkWord(word: string): Promise<CheckWordResult> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/moderation/check`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text: word }),
    });

    if (!response.ok) {
      return { ok: false, message: UNAVAILABLE_MESSAGE };
    }

    const body: unknown = await response.json();
    const parsed = asModerationCheckResponse(body);
    if (!parsed) {
      return { ok: false, message: UNAVAILABLE_MESSAGE };
    }

    return { ok: parsed.allowed, message: parsed.message ?? undefined };
  } catch {
    // Network failure, JSON parse failure, or any other unexpected rejection:
    // fail closed rather than let an unchecked word through.
    return { ok: false, message: UNAVAILABLE_MESSAGE };
  }
}
