// ----------------------------------------------------------------------------
//  analyticsEvents.ts - the anonymous GA4 event VOCABULARY + the pure param
//  allowlist builder (analytics/01, AC-02 / AC-07).
//
//  This is the AC-02 guarantee made testable: GA4 events must carry ONLY
//  allowlisted, anonymous, enum-ish facts - a mode id, solo/group context, a
//  reaction/share-method label, a player-count bucket - and NEVER a nickname, join
//  code, player/session id, submitted word, or story text (README section 6). The
//  event NAMES are a fixed set (no free-text event names), and buildEventParams()
//  below is a PURE function that copies through ONLY the allowlisted keys and
//  drops everything else - so even a loosened/casted caller cannot smuggle an
//  identity field into an event. Mirrors usageBeacon.ts's buildUsagePayload: the
//  no-PII shape is proven by a unit test, not just asserted in a comment.
//
//  Why an allowlist and not a denylist: the server's PII scrubber
//  (api/src/Telemetry/PiiScrubbingTelemetryInitializer.cs) is a denylist backstop,
//  but GA4 is a THIRD-PARTY sink with no server choke point in front of it, so the
//  browser is the only place to enforce "no PII" - and here it is an allowlist
//  (default-deny), the stricter posture for an un-scrubbed egress.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/**
 * The fixed set of GA4 event names (snake_case per GA4 convention). A closed set,
 * so an event name is never assembled from anything player-supplied. These are the
 * funnel + frustration moments the alpha needs (AC-07):
 *   - room_created / room_joined - the group-play funnel entry.
 *   - round_started / reveal_reached - the core play loop (with mode + context).
 *   - reaction_tapped - engagement at the payoff moment.
 *   - invite_shared - the share/keepsake loop.
 *   - reconnect_shown / seat_timed_out - the frustration signals (dead zones,
 *     expired seats) that App Insights sees as disconnects but GA4 can put in a
 *     behavioral funnel.
 */
export const ANALYTICS_EVENTS = {
  RoomCreated: 'room_created',
  RoomJoined: 'room_joined',
  RoundStarted: 'round_started',
  RevealReached: 'reveal_reached',
  ReactionTapped: 'reaction_tapped',
  InviteShared: 'invite_shared',
  ReconnectShown: 'reconnect_shown',
  SeatTimedOut: 'seat_timed_out',
} as const;

/** A valid GA4 event name (one of the fixed ANALYTICS_EVENTS values). */
export type AnalyticsEventName = (typeof ANALYTICS_EVENTS)[keyof typeof ANALYTICS_EVENTS];

/**
 * The ONLY params an analytics event may carry. Every field is anonymous and
 * enum-ish by construction - there is deliberately NO field for a nickname, code,
 * word, or any identity. All optional: an event sends only what is relevant.
 */
export interface AnalyticsEventParams {
  /** The stable, enum-ish mode id (e.g. "classic-blind") - never free text. */
  mode?: string;
  /** Solo vs group context - the same axis platform-devops/05 already records. */
  context?: 'solo' | 'group';
  /** The reaction pill id (e.g. "laugh") - an enum-ish label, never free text. */
  reaction?: string;
  /** The share method (e.g. "copy-link", "share-sheet") - enum-ish, never free text. */
  method?: string;
  /** An approximate player count (a small integer) - a count, never an identity. */
  players?: number;
}

/** The allowlisted param keys - the WHOLE surface an event may carry. */
const ALLOWED_STRING_KEYS = ['mode', 'context', 'reaction', 'method'] as const;

/** Defensive cap so a value can never carry a paragraph of anything (AC-02). */
const MAX_VALUE_LENGTH = 40;

/**
 * Build the GA4 event params from the typed input, copying through ONLY the
 * allowlisted keys and dropping everything else (AC-02). PURE and testable (this
 * is the guarantee): it reads a fixed set of keys, keeps a string value only when
 * it is a non-empty, reasonably-short string, and keeps `players` only when it is
 * a finite non-negative number (clamped) - so no unexpected/identity-bearing field
 * can survive, exactly like usageBeacon.ts's buildUsagePayload. Accepts a loosened
 * record too (not just the typed shape) so the test can feed it PII-shaped junk and
 * prove it is stripped.
 */
export function buildEventParams(
  raw: AnalyticsEventParams | Record<string, unknown>,
): Record<string, string | number> {
  const source = raw as Record<string, unknown>;
  const clean: Record<string, string | number> = {};

  for (const key of ALLOWED_STRING_KEYS) {
    const value = source[key];
    if (typeof value === 'string' && value.length > 0 && value.length <= MAX_VALUE_LENGTH) {
      clean[key] = value;
    }
  }

  const players = source.players;
  if (typeof players === 'number' && Number.isFinite(players) && players >= 0) {
    // Clamp to a small integer - a count, never a precise fingerprint.
    clean.players = Math.min(99, Math.floor(players));
  }

  return clean;
}
