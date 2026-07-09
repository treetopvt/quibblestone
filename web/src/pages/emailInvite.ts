// ----------------------------------------------------------------------------
//  emailInvite.ts - the thin web REST client for the email-a-game-invite channel
//  (session-engine/12, issue #180). NOT the feature: the real send (shape validation,
//  the server-built join link, the ONE IEmailSender seam, the per-IP rate limit) lives
//  server-side in api/src/Controllers/EmailInviteController.cs. This module only probes
//  availability and POSTs { roomCode, toEmail }.
//
//  Mirrors web/src/gallery/publishTale.ts: the
//  API base URL comes from `import.meta.env.VITE_API_BASE_URL` (never hardcoded,
//  CLAUDE.md section 4), and every call FAILS GRACEFULLY - a network error, non-OK
//  status, or unparseable body resolves to a typed result (availability -> false; a
//  send -> { ok: false, message }) rather than throwing, so the Lobby's Copy/Share
//  path is never blocked or broken by this optional third channel.
//
//  Child safety / privacy (AC-04): the request carries ONLY the room code and the
//  recipient address the sender typed - no free text, no nickname, no PII about a
//  player. The recipient address is used for this one send and never stored.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** The API base URL (from Vite env, never hardcoded - CLAUDE.md section 4). */
const base = () => import.meta.env.VITE_API_BASE_URL;

/** Friendly fallback shown when a send fails with no server-supplied message. */
const GENERIC_SEND_ERROR =
  "We couldn't send that invite just now - please try Copy or Share instead.";

/** Friendly message for a 429 (too many invites from this device in a short window). */
const RATE_LIMITED_MESSAGE =
  "That's a lot of invites at once - give it a minute, then try again.";

/**
 * Narrows the GET /api/invite/availability body into a boolean. Defaults to false
 * (fail toward HIDDEN, AC-06) for anything that is not an explicit `{ available: true }`,
 * so the control never shows unless email is genuinely configured. Exported for a
 * direct Vitest test (mirrors useRoomInvite.test.ts's resolveOrigin coverage).
 */
export function narrowAvailability(value: unknown): boolean {
  if (typeof value !== 'object' || value === null) return false;
  return (value as Record<string, unknown>).available === true;
}

/**
 * Probes whether email invites can be sent (GET /api/invite/availability). Resolves
 * false on ANY failure (network, non-OK, unparseable, or available:false) so the Lobby
 * fails toward hiding the control (AC-06). Never throws.
 */
export async function fetchEmailInviteAvailable(): Promise<boolean> {
  try {
    const response = await fetch(`${base()}/api/invite/availability`);
    if (!response.ok) return false;
    return narrowAvailability(await response.json().catch(() => null));
  } catch {
    return false;
  }
}

/** The outcome of a send attempt: `ok` on success, else a friendly `message` to show. */
export interface EmailInviteSendResult {
  ok: boolean;
  message?: string;
}

/**
 * Narrows the POST /api/invite/email response (its status + parsed body) into a typed
 * result. `{ sent: true }` -> ok; a 429 -> the rate-limited message; anything else ->
 * not-ok with the server's friendly message (or a generic fallback). Pure and exported
 * for a direct Vitest test.
 */
export function narrowSendResult(status: number, body: unknown): EmailInviteSendResult {
  const record =
    typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {};
  const message =
    typeof record.message === 'string' && record.message.length > 0 ? record.message : undefined;

  if (record.sent === true) return { ok: true };
  if (status === 429) return { ok: false, message: message ?? RATE_LIMITED_MESSAGE };
  return { ok: false, message: message ?? GENERIC_SEND_ERROR };
}

/**
 * Emails the room's join link + code to one address (POST /api/invite/email). Resolves
 * a typed { ok, message? } for any outcome (success, bad input, rate-limited, provider
 * off, network error) - never throws, so the caller can show a friendly line and the
 * rest of the invite widget keeps working (AC-01/AC-06).
 */
export async function sendEmailInvite(
  roomCode: string,
  toEmail: string,
): Promise<EmailInviteSendResult> {
  try {
    const response = await fetch(`${base()}/api/invite/email`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ roomCode, toEmail }),
    });
    return narrowSendResult(response.status, await response.json().catch(() => null));
  } catch {
    return { ok: false, message: GENERIC_SEND_ERROR };
  }
}
