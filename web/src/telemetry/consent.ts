// ----------------------------------------------------------------------------
//  consent.ts - the device-local analytics-consent choice (analytics/01, AC-05).
//
//  Product analytics (GA4 + Clarity, see analytics.ts) is NON-ESSENTIAL and
//  kid-facing, so it must be CONSENT-GATED (README section 6). This module is the
//  single home for that choice: a tiny, versioned, device-local record of whether
//  the player has granted or denied analytics - nothing loads a tracking cookie or
//  sends an event until this says "granted" (via Google Consent Mode v2 in
//  analytics.ts).
//
//  THREE STATES: 'granted', 'denied', or 'unset' (no choice made yet). 'unset' is
//  what drives the consent banner to appear; a made choice hides it and persists.
//  An 'unset' player's EFFECTIVE consent is ANALYTICS_DEFAULT_CONSENT below - the
//  one knob an operator flips to run a closed friends-and-family test as opt-OUT
//  ('granted' by default, banner still shown so a tester can decline) versus a
//  public opt-IN ('denied' until Accept). Mirrors content/familySafe.ts's
//  FAMILY_SAFE_DEFAULT: one documented token, not a convention repeated per site.
//
//  POSTURE (mirrors identity.ts / deviceId.ts): device-local, anonymous, account-
//  free, versioned key, and EVERY storage access wrapped in try/catch because
//  localStorage can throw or be absent (private-browsing, disabled storage, quota,
//  SSR). The stored value is VALIDATED on load and never trusted blindly - an
//  unknown/corrupt entry reads as 'unset' rather than being mis-used. This is NOT
//  PII and NOT an account: it is one small string about a preference on one device.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** A made analytics-consent decision. */
export type ConsentChoice = 'granted' | 'denied';

/** The stored consent state: a made choice, or 'unset' when none exists yet. */
export type StoredConsent = ConsentChoice | 'unset';

/**
 * The effective consent for a player who has NOT chosen yet (AC-05). Mirrors
 * content/familySafe.ts's FAMILY_SAFE_DEFAULT - flip this ONE token to change the
 * default posture:
 *   - 'granted' (current, for initial rollout): opt-OUT. An unset player is
 *     measured from load (Consent Mode starts granted) so monitoring is LIVE the
 *     moment the operator sets the measurement ids - the deliberate choice for the
 *     friends-and-family rollout (the banner is deferred, see below).
 *   - 'denied': true opt-IN. Nothing is measured until a player accepts. The
 *     privacy-forward posture to switch to for PUBLIC launch (pair it with
 *     ANALYTICS_SHOW_CONSENT_BANNER = true).
 * This only changes what happens BEFORE a player makes a choice; a made choice
 * (via the banner, once enabled) always wins and persists.
 */
export const ANALYTICS_DEFAULT_CONSENT: ConsentChoice = 'granted';

/**
 * Whether to show the lightweight one-time consent banner (analytics/01, AC-05).
 * Deferred for the initial rollout per product direction: monitoring is live
 * (ANALYTICS_DEFAULT_CONSENT = 'granted') and the banner is OFF, so nothing is
 * shown to a player yet. Flip to `true` to turn the banner on later - it then
 * appears ONCE for a player who has not chosen (loadConsent() === 'unset'), is
 * light and unobtrusive (see ConsentBanner.tsx), and never shows again after they
 * acknowledge or opt out. For a public opt-IN launch, set this `true` AND set
 * ANALYTICS_DEFAULT_CONSENT = 'denied'.
 */
export const ANALYTICS_SHOW_CONSENT_BANNER = false;

// Versioned key: bump the suffix if the stored shape ever changes so an old entry
// is simply replaced rather than mis-read (mirrors identity.ts's STORAGE_KEY).
const STORAGE_KEY = 'qs.analytics.consent.v1';

/**
 * True when the value is a made consent choice. PURE (no window access) so the
 * validation guarantee is unit-testable without a DOM.
 */
export function isConsentChoice(value: unknown): value is ConsentChoice {
  return value === 'granted' || value === 'denied';
}

/**
 * Load the player's analytics-consent choice, or 'unset' when none exists, storage
 * is unavailable, or the stored value fails validation. Never throws: any storage
 * or parse error resolves to 'unset' (so the banner shows and nothing is assumed).
 */
export function loadConsent(): StoredConsent {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return isConsentChoice(raw) ? raw : 'unset';
  } catch {
    // Storage disabled / quota / SSR: treat as "no choice made".
    return 'unset';
  }
}

/**
 * Persist the player's analytics-consent choice (device-local only). Silently
 * no-ops if storage is unavailable - a failed write just means the banner may show
 * again next visit, never a broken flow.
 */
export function saveConsent(choice: ConsentChoice): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, choice);
  } catch {
    // Ignore: persistence here is best-effort, never a requirement.
  }
}

/**
 * Resolve a stored state to the EFFECTIVE consent used to gate loading + sending
 * (AC-05). PURE and testable: an 'unset' player resolves to
 * ANALYTICS_DEFAULT_CONSENT; a made choice resolves to itself.
 */
export function effectiveConsent(stored: StoredConsent): ConsentChoice {
  return stored === 'unset' ? ANALYTICS_DEFAULT_CONSENT : stored;
}
