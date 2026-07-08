// ----------------------------------------------------------------------------
//  analytics.ts - client-side product analytics: GA4 + Microsoft Clarity
//  (analytics/01). The ONE module that owns loading, consent, and sending.
//
//  WHY CLIENT-SIDE (and why it is a departure): "click patterns" and rage/dead-
//  click frustration only exist in the browser, so unlike the SERVER-side App
//  Insights pipeline (platform-devops/04-05) this is deliberately a client concern
//  - gtag.js for GA4 event funnels + route views, and Clarity's tag for session
//  replay + heatmaps. platform-devops/05 parked third-party analytics SDKs partly
//  over child-privacy; this module is how that concern is ANSWERED, not ignored -
//  see the four guardrails below and docs/features/analytics/feature.md.
//
//  GUARDRAILS (all enforced here):
//    1. ENV-GATED / NO-OP (AC-01). With neither VITE_GA4_MEASUREMENT_ID nor
//       VITE_CLARITY_PROJECT_ID set, nothing loads and every function is a no-op -
//       exactly errorBeacon.ts's "no-op when unconfigured" posture. Merging this
//       changes nothing until an operator sets an id.
//    2. CONSENT-GATED (AC-05). Google Consent Mode v2 defaults to DENIED; GA4
//       loads but sends nothing, and Clarity does not load at all, until consent
//       is granted (see setAnalyticsConsent). consent.ts owns the choice.
//    3. ANONYMOUS BY CONSTRUCTION (AC-02/AC-06). Events carry only the allowlisted
//       params from analyticsEvents.ts; page_view uses a NORMALIZED route (reusing
//       errorBeacon.ts's normalizeRoutePath) and a SCRUBBED page_location, so a
//       deep-link join code (/join/ABCD) never reaches GA4. GA4 runs with IP
//       anonymization + Google Signals OFF + ad personalization OFF (AC-04).
//    4. CLARITY MASKS ALL TEXT (AC-03). Clarity is loaded once consent is granted
//       and NEVER unmasked from code. Typed free-text words - the one real child-
//       data surface - must never appear in a replay. IMPORTANT OPERATOR STEP: the
//       Clarity PROJECT itself must be created with Masking = "Mask" (strict);
//       per-element masking is not fully controllable from JS, so the project
//       setting is the real guarantee. We additionally tag the word-entry field
//       with data-clarity-mask as defense-in-depth (see the fill-blank input).
//
//  OPERATOR STEPS (config-side guarantees the app CANNOT enforce from JS - do BOTH
//  before turning the ids on; also in docs/features/analytics/feature.md):
//    - CLARITY: set the project's Masking to "Mask" (strict) so no typed text is
//      recorded (guardrail #4 is the code-side half; this is the real guarantee).
//    - GA4: turn OFF Enhanced Measurement's "page changes based on browser history
//      events". The app sends its own SCRUBBED page_views (trackPageView), so
//      leaving GA4's automatic SPA page tracking on would (a) double-count and,
//      worse, (b) LEAK the join code - an auto page_view on the /join/ABCD ->
//      /lobby navigation carries page_referrer = the /join/ABCD url. Disabling it
//      leaves only our scrubbed sends. (trackPageView also scrubs page_referrer
//      defensively, but this property setting is the real fix.)
//
//  FIRE-AND-FORGET / NEVER BLOCKS (AC-08): scripts load async; every send is
//  wrapped so a blocked/slow analytics host can never delay first paint, wedge a
//  round, or surface to a player - the same posture as errorBeacon.ts / serveLog.ts.
//
//  Config: measurement ids come from import.meta.env (VITE_*, typed in
//  web/src/vite-env.d.ts). They are PUBLIC ids (not secrets), so unlike connection
//  strings they are fine baked into the bundle (README section 4 / deploy.yml).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { normalizeRoutePath } from './errorBeacon';
import {
  ANALYTICS_SHOW_CONSENT_BANNER,
  effectiveConsent,
  loadConsent,
  saveConsent,
} from './consent';
import { buildEventParams, type AnalyticsEventName, type AnalyticsEventParams } from './analyticsEvents';

/** The gtag / clarity command-queue functions, typed without `any` (variadic). */
type Gtag = (...args: unknown[]) => void;
type ClarityApi = (...args: unknown[]) => void;

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: Gtag;
    clarity?: ClarityApi & { q?: unknown[] };
  }
}

/** Trim an env id to `string | undefined` (empty / whitespace reads as unset). */
function readId(value: string | undefined): string | undefined {
  const trimmed = (value ?? '').trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

// Read once at module load. Unset in tests (jsdom has no VITE_* vars), so the
// whole module is inert there - only the pure builders (analyticsEvents.ts,
// consent.ts) are unit-tested, exactly like usageBeacon.ts's split.
const GA4_ID = readId(import.meta.env.VITE_GA4_MEASUREMENT_ID);
const CLARITY_ID = readId(import.meta.env.VITE_CLARITY_PROJECT_ID);

/** True when at least one provider is configured (drives AC-01 no-op + the banner). */
export const analyticsConfigured = GA4_ID !== undefined || CLARITY_ID !== undefined;

let initialized = false;
let clarityLoaded = false;

/** Inject an async <script>; swallow every failure (analytics is best-effort). */
function injectScript(src: string): void {
  try {
    const el = document.createElement('script');
    el.async = true;
    el.src = src;
    document.head.appendChild(el);
  } catch {
    // A CSP / DOM / network failure here is a no-op - never surfaces to a player.
  }
}

/** Ensure the gtag command queue exists (idempotent). gtag.js replays dataLayer. */
function ensureGtag(): void {
  if (typeof window.gtag === 'function') {
    return;
  }
  window.dataLayer = window.dataLayer ?? [];
  // gtag.js drains window.dataLayer when it loads and replays each queued entry as a
  // gtag() call - and it expects each entry to be that call's ARGUMENTS object, the
  // exact form of Google's canonical snippet (`function gtag(){dataLayer.push(arguments)}`).
  // An earlier version pushed a plain rest-param ARRAY, which gtag.js's queue drain
  // does not always replay - the tag loaded but recorded nothing (Clarity, which uses
  // its own queue, was unaffected). Pushing `arguments` requires a classic function
  // (arrow functions have no `arguments`).
  function gtag(): void {
    // eslint-disable-next-line prefer-rest-params
    window.dataLayer?.push(arguments);
  }
  window.gtag = gtag;
}

/** Load Clarity once (only ever called when consent is granted). Never unmasks. */
function loadClarity(projectId: string): void {
  if (clarityLoaded) {
    return;
  }
  clarityLoaded = true;
  try {
    if (typeof window.clarity !== 'function') {
      const shim = ((...args: unknown[]): void => {
        (shim.q = shim.q ?? []).push(args);
      }) as ClarityApi & { q?: unknown[] };
      window.clarity = shim;
    }
    injectScript(`https://www.clarity.ms/tag/${encodeURIComponent(projectId)}`);
    // We only reach here after consent is granted, so signal cookie consent.
    window.clarity('consent');
  } catch {
    // Best-effort: a failed Clarity load never affects the app.
  }
}

/**
 * Install analytics ONCE at startup (main.tsx, beside installErrorBeacon). No-op
 * when unconfigured (AC-01) or outside a browser. Sets Consent Mode v2 defaults
 * BEFORE any provider loads (AC-05), then loads GA4 (which respects consent) and -
 * only if consent is already effectively granted - Clarity. Idempotent.
 */
export function initAnalytics(): void {
  if (initialized || typeof window === 'undefined') {
    return;
  }
  initialized = true;
  if (!analyticsConfigured) {
    return;
  }

  const consentGranted = effectiveConsent(loadConsent()) === 'granted';

  // Consent Mode v2 defaults FIRST (AC-05). ad_* stay denied always - this is a
  // kid-facing property with no ads (README section 3). analytics_storage follows
  // the current consent, so a returning granter is measured and an unset/denied
  // player is not, until they accept.
  ensureGtag();
  window.gtag?.('consent', 'default', {
    ad_storage: 'denied',
    ad_user_data: 'denied',
    ad_personalization: 'denied',
    analytics_storage: consentGranted ? 'granted' : 'denied',
    functionality_storage: 'granted',
    security_storage: 'granted',
  });

  if (GA4_ID !== undefined) {
    injectScript(`https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(GA4_ID)}`);
    window.gtag?.('js', new Date());
    // AC-04 hardening. anonymize_ip is a no-op in GA4 (it anonymizes IP by default)
    // but is set to document intent; Signals + ad personalization are turned OFF so
    // no cross-site/advertising profile is built. send_page_view:false because the
    // SPA sends its own scrubbed page_view on each route change (trackPageView).
    window.gtag?.('config', GA4_ID, {
      anonymize_ip: true,
      allow_google_signals: false,
      allow_ad_personalization_signals: false,
      send_page_view: false,
    });
  }

  if (CLARITY_ID !== undefined && consentGranted) {
    loadClarity(CLARITY_ID);
  }
}

/**
 * Record the player's consent choice (analytics/01, AC-05) - called by the consent
 * banner. Persists device-local, updates Consent Mode, and (on grant) loads Clarity
 * if it was deferred. Safe to call when unconfigured (persists the choice, no send).
 *
 * REVOKE LIMITATION (resolve before enabling the banner): a grant -> deny updates
 * GA4 Consent Mode to denied (GA4 stops), but Clarity, once loaded, keeps recording
 * until the next page load - there is no clean JS stop. This is LATENT today: the
 * banner is off and the rollout default is opt-OUT granted, so the only grant->deny
 * path is the not-yet-enabled banner. Before setting ANALYTICS_SHOW_CONSENT_BANNER
 * = true, handle this (e.g. reload the page on revoke).
 */
export function setAnalyticsConsent(granted: boolean): void {
  saveConsent(granted ? 'granted' : 'denied');
  if (!analyticsConfigured || typeof window === 'undefined') {
    return;
  }
  try {
    ensureGtag();
    window.gtag?.('consent', 'update', {
      analytics_storage: granted ? 'granted' : 'denied',
    });
  } catch {
    // swallow - consent update is best-effort
  }
  if (granted && CLARITY_ID !== undefined) {
    loadClarity(CLARITY_ID);
  }
}

/**
 * True when the lightweight consent banner should be shown (AC-05): analytics is
 * configured, the banner is enabled (ANALYTICS_SHOW_CONSENT_BANNER - OFF for the
 * initial rollout, see consent.ts), AND the player has not chosen yet. Deferred by
 * default, so this is false during the initial monitoring-live rollout. Pure read;
 * the banner re-checks on mount.
 */
export function shouldPromptConsent(): boolean {
  return analyticsConfigured && ANALYTICS_SHOW_CONSENT_BANNER && loadConsent() === 'unset';
}

/**
 * Send one anonymous GA4 event (AC-07). No-op when GA4 is unconfigured or consent
 * is not effectively granted. Params run through the allowlist builder (AC-02) so
 * no identity field can ride an event. Fire-and-forget: any failure is swallowed.
 */
export function trackEvent(name: AnalyticsEventName, params: AnalyticsEventParams = {}): void {
  if (GA4_ID === undefined || typeof window === 'undefined') {
    return;
  }
  if (effectiveConsent(loadConsent()) !== 'granted') {
    return;
  }
  try {
    window.gtag?.('event', name, buildEventParams(params));
  } catch {
    // swallow - an event must never surface to a player or wedge a flow
  }
}

/**
 * Send a GA4 page_view for an SPA route change (AC-06). No-op when GA4 is
 * unconfigured or consent is not granted. The path is NORMALIZED to its top-level
 * segment (reusing errorBeacon.ts's normalizeRoutePath) and set as the default
 * page_location/page_path via gtag('set'), so a deep-link join code (/join/ABCD)
 * never reaches GA4 - on this page_view OR any later auto event. page_title is
 * pinned so no dynamic title can leak. Fire-and-forget.
 */
export function trackPageView(pathname: string): void {
  if (GA4_ID === undefined || typeof window === 'undefined') {
    return;
  }
  if (effectiveConsent(loadConsent()) !== 'granted') {
    return;
  }
  try {
    const path = normalizeRoutePath(pathname);
    const origin = window.location?.origin ?? '';
    window.gtag?.('set', {
      page_path: path,
      page_location: `${origin}${path}`,
      // Scrub the referrer default to the bare origin so no previous route (a
      // /join/ABCD deep link) rides along as page_referrer on this or a later
      // event. Defense-in-depth; the real fix for GA4's own auto SPA page_views is
      // the operator step in this file's header (disable Enhanced Measurement).
      page_referrer: origin,
      page_title: 'QuibbleStone',
    });
    window.gtag?.('event', 'page_view');
  } catch {
    // swallow - a page_view must never wedge navigation
  }
}
