// ----------------------------------------------------------------------------
//  reconnect.ts - remember this device's held seat, device-local only.
//
//  session-engine/07 (server) marks a dropped connection's seat "disconnected"
//  instead of evicting it right away, and mints a per-seat reconnect token that
//  is returned ONLY to that seat's own CreateRoom/JoinRoom result envelope.
//  session-engine/08 adds the `Rejoin(code, token)` hub method that spends that
//  token to reclaim the seat on a new connection. This module is where the WEB
//  client remembers the `{code, token}` pair between "the connection dropped"
//  and "the client calls Rejoin" - session-engine/09 wires the calling side in
//  `useGameHub.ts`.
//
//  This is DEVICE-LOCAL CONVENIENCE, same posture as `identity.ts`: no account,
//  no server record beyond the room's own in-memory seat, nothing that survives
//  the room's ephemeral lifetime once the seat is evicted or the token is spent.
//  It stores exactly ONE `{code, token}` pair - two opaque strings, nothing else
//  (no nickname, no name, no cross-room history) - and OVERWRITES whatever was
//  there before: a device holds at most one "current seat" handle at a time
//  (AC-01, AC-06). It never leaves the device except as the `Rejoin` invoke's
//  own two arguments.
//
//  IMPORTANT: this is a SEPARATE, differently-versioned key from `identity.ts`.
//  `identity.ts` remembers a NICKNAME + GUARDIAN VARIANT - cosmetic pre-fill for
//  a form, harmless to lose. This module remembers a LIVE CAPABILITY HANDLE -
//  losing it means a mid-game reconnect falls back to a fresh join instead of
//  resuming the same seat, and a stale one left lying around after a deliberate
//  leave must never haunt a later create/join. The two must never be conflated
//  or cleared together.
//
//  Robustness: mirrors identity.ts exactly. Every localStorage access is
//  wrapped in try/catch (it can throw or be absent - private browsing, disabled
//  storage, quota, SSR). On load we VALIDATE the parsed shape - `code` and
//  `token` must both be non-empty strings - and fall back to null on anything
//  unexpected (corrupt JSON, an old shape, a missing field), never trusting the
//  stored bytes blindly and never using a non-null assertion.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

// Versioned key: bump the suffix if the stored shape ever changes, so an old
// entry is simply ignored (loadReconnectHandle returns null) rather than
// mis-read. Deliberately distinct from identity.ts's `qs.identity.v1`.
const STORAGE_KEY = 'qs.reconnect.v1';

/** The remembered "current seat" handle: a room code + this seat's own reconnect token. */
export interface ReconnectHandle {
  code: string;
  token: string;
}

/** True for a non-empty string - the only shape either field of a handle may take. */
function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

/**
 * Load the stored reconnect handle from device-local storage, or null when
 * there is none, storage is unavailable, or the stored value fails
 * validation. Never throws: any storage or parse error resolves to null (the
 * caller then behaves as if no seat is held).
 */
export function loadReconnectHandle(): ReconnectHandle | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) {
      return null;
    }

    // Parse into `unknown` and narrow by hand - never trust the stored shape.
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null) {
      return null;
    }

    const record = parsed as Record<string, unknown>;
    const { code, token } = record;
    if (!isNonEmptyString(code) || !isNonEmptyString(token)) {
      return null;
    }

    return { code, token };
  } catch {
    // Storage unavailable / disabled, quota, or malformed JSON - treat as "none".
    return null;
  }
}

/**
 * Remember the given room code + reconnect token as this device's current
 * seat (device-local only). Called on a SUCCESSFUL create / join, alongside
 * the existing room/isHost updates. OVERWRITES whatever handle was stored
 * before - a device holds at most one "current seat" at a time (AC-01).
 * Silently no-ops if storage is unavailable - persistence here is a
 * convenience, never a requirement, and must never break the create/join it
 * follows.
 */
export function saveReconnectHandle(code: string, token: string): void {
  try {
    const value: ReconnectHandle = { code, token };
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
  } catch {
    // Ignore: a failed write just means there is nothing to auto-rejoin with later.
  }
}

/**
 * Forget the stored reconnect handle (device-local only). Called whenever the
 * handle should no longer be spent: a deliberate leave / return Home (AC-05),
 * or a failed `Rejoin` attempt so a stale token never haunts a later create/
 * join (AC-04). Silently no-ops if storage is unavailable.
 */
export function clearReconnectHandle(): void {
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Ignore: nothing to clear if storage never worked in the first place.
  }
}
