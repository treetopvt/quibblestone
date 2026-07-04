// ----------------------------------------------------------------------------
//  reportedTalesClient.ts - the web client for the OPERATOR review queue of
//  reported public tales (sysadmin-console/03, issue #137). A thin REST client, NOT
//  the feature: the real moderation state, the auto-hide threshold, and the operator
//  authorization all live server-side (api/src/Admin/ReportedTalesController behind
//  story 01's "Operator" policy). This module only GETs the queue and POSTs the two
//  operator actions (confirm / restore), and shapes their responses.
//
//  SEPARATE ADMIN BUNDLE (AC-04, from story 01): this file lives in the admin bundle
//  and imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components), and the kid app imports nothing from here. The API base URL comes
//  from `import.meta.env.VITE_API_BASE_URL` (never hardcoded, never a secret). Every
//  call sends credentials so the HttpOnly operator cookie rides along, and FAILS
//  GRACEFULLY - a network error, non-OK status, or unparseable body resolves to a
//  friendly result rather than throwing, so the queue never shows a raw error.
//
//  ANONYMITY (AC-06): the queue is CONTENT + a count. This client neither requests
//  nor exposes any reporter identity, player nickname, room, or session.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** One ordered body part of a reported tale (matches the server ReportedTalePartDto). */
export interface ReportedTalePart {
  /** True for a coral player-word, false for literal template text. */
  isWord: boolean;
  /** The already-filtered part text. */
  text: string;
}

/** One entry of the operator review queue: a hidden tale's content + report count. */
export interface ReportedTale {
  /** The hidden tale's slug (the key the confirm / restore actions target). */
  slug: string;
  /** The tale title. */
  title: string;
  /** The ordered body: literal text interleaved with coral player-words. */
  parts: ReportedTalePart[];
  /** The in-session nickname byline (may be empty). */
  bylineNames: string;
  /** How many reports pushed the tale past the auto-hide threshold. */
  reportCount: number;
}

/** The result of loading the review queue. */
export interface ReviewQueueResult {
  /** True when the queue loaded (even if empty); false on any transport / auth failure. */
  ok: boolean;
  /** The hidden tales awaiting a decision (empty when none, or on a failure). */
  tales: ReportedTale[];
  /** A friendly message to show on a failure (empty on success). */
  message: string;
}

/** The result of a confirm / restore action. */
export interface ReportedTaleActionResult {
  /** True when the action reached the server and returned OK. */
  ok: boolean;
  /** True when a hidden tale was actually found and acted on (server Applied). */
  applied: boolean;
  /** A friendly message describing the outcome. */
  message: string;
}

/** Friendly fallback shown when the review endpoints cannot be reached or parsed. */
const UNAVAILABLE_MESSAGE =
  'We could not reach the review queue just now - please try again in a moment.';

/** The API base URL, from a NON-secret VITE_ var (the allowlist / keys are never here). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

/** Narrows one unknown part into a ReportedTalePart, or null if malformed. */
function asPart(value: unknown): ReportedTalePart | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.isWord !== 'boolean' || typeof record.text !== 'string') return null;
  return { isWord: record.isWord, text: record.text };
}

/** Narrows one unknown queue entry into a ReportedTale, or null if malformed. */
function asReportedTale(value: unknown): ReportedTale | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.slug !== 'string' || typeof record.title !== 'string') return null;
  if (typeof record.reportCount !== 'number') return null;
  const rawParts = Array.isArray(record.parts) ? record.parts : [];
  const parts: ReportedTalePart[] = [];
  for (const raw of rawParts) {
    const part = asPart(raw);
    if (part) parts.push(part);
  }
  const bylineNames = typeof record.bylineNames === 'string' ? record.bylineNames : '';
  return { slug: record.slug, title: record.title, parts, bylineNames, reportCount: record.reportCount };
}

/**
 * Loads the operator review queue (GET /api/admin/reported-tales). Resolves the list
 * of currently-hidden tales on success, or a friendly failure on any transport / auth
 * error - never throws. Sends credentials so the operator cookie is included.
 */
export async function loadReviewQueue(): Promise<ReviewQueueResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/reported-tales`, {
      method: 'GET',
      credentials: 'include',
    });
    if (!response.ok) {
      return { ok: false, tales: [], message: UNAVAILABLE_MESSAGE };
    }
    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) {
      return { ok: false, tales: [], message: UNAVAILABLE_MESSAGE };
    }
    const rawTales = (body as Record<string, unknown>).tales;
    const list = Array.isArray(rawTales) ? rawTales : [];
    const tales: ReportedTale[] = [];
    for (const raw of list) {
      const tale = asReportedTale(raw);
      if (tale) tales.push(tale);
    }
    return { ok: true, tales, message: '' };
  } catch {
    return { ok: false, tales: [], message: UNAVAILABLE_MESSAGE };
  }
}

/** Narrows an unknown action response body. */
function asActionResult(value: unknown): { applied: boolean; message: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.applied !== 'boolean') return null;
  const message = typeof record.message === 'string' ? record.message : '';
  return { applied: record.applied, message };
}

/** Shared POST for the two operator actions (confirm / restore). */
async function postAction(slug: string, action: 'confirm' | 'restore'): Promise<ReportedTaleActionResult> {
  try {
    const response = await fetch(
      `${API_BASE_URL}/api/admin/reported-tales/${encodeURIComponent(slug)}/${action}`,
      { method: 'POST', credentials: 'include' },
    );
    if (!response.ok) {
      return { ok: false, applied: false, message: UNAVAILABLE_MESSAGE };
    }
    const body: unknown = await response.json();
    const parsed = asActionResult(body);
    if (!parsed) {
      return { ok: false, applied: false, message: UNAVAILABLE_MESSAGE };
    }
    return { ok: true, applied: parsed.applied, message: parsed.message };
  } catch {
    return { ok: false, applied: false, message: UNAVAILABLE_MESSAGE };
  }
}

/**
 * Confirms a hidden tale stays gone (POST /api/admin/reported-tales/{slug}/confirm).
 * After this the slug never serves again. Never throws.
 */
export function confirmHiddenTale(slug: string): Promise<ReportedTaleActionResult> {
  return postAction(slug, 'confirm');
}

/**
 * Restores a hidden tale (POST /api/admin/reported-tales/{slug}/restore). It resumes
 * serving normally and its report count is reset. Never throws.
 */
export function restoreHiddenTale(slug: string): Promise<ReportedTaleActionResult> {
  return postAction(slug, 'restore');
}
