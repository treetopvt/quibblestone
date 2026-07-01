// ----------------------------------------------------------------------------
//  identity.ts - remember a player's last-used name + Guardian, device-local only.
//
//  build/host-identity: a small convenience so a returning player (host OR joiner)
//  does not retype their name + avatar every session. We persist ONLY two tiny
//  strings - the in-session nickname and the chosen Guardian variant - under a
//  versioned localStorage key, and pre-fill the HostSetup / Join forms from them.
//
//  This is DEVICE-LOCAL CONVENIENCE, nothing more. It is NOT PII off-device, NOT
//  an account, NOT a server record, and NOT a substitute for the server-side
//  content-safety filter: the hub still trims, length-checks, and safety-filters
//  the name authoritatively on CreateRoom / JoinRoom before it is ever stored or
//  shown (child safety, README section 6 / CLAUDE.md section 5). A nickname + a
//  variant are two tiny strings, so localStorage is exactly the right tool - no
//  database, no new dependency (no localforage), no PII ever leaves the device.
//
//  Robustness: every localStorage access is wrapped in try/catch because it can
//  throw or be absent (private-browsing modes, disabled storage, quota, SSR).
//  On load we VALIDATE the parsed shape - nickname must be a string, variant must
//  be one of the six known GuardianVariants - and fall back to null on anything
//  unexpected (a corrupt entry, an old shape, an unknown variant), never trusting
//  the stored bytes blindly and never using a non-null assertion.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { GUARDIAN_VARIANTS } from './components';
import type { GuardianVariant } from './components';

// Versioned key: bump the suffix if the stored shape ever changes, so an old
// entry is simply ignored (loadIdentity returns null) rather than mis-read.
const STORAGE_KEY = 'qs.identity.v1';

/** The remembered identity: an anonymous nickname + a known Guardian variant. */
export interface StoredIdentity {
  nickname: string;
  variant: GuardianVariant;
}

/** True when the value is one of the six known Guardian variants. */
function isGuardianVariant(value: unknown): value is GuardianVariant {
  return (
    typeof value === 'string' &&
    (GUARDIAN_VARIANTS as readonly string[]).includes(value)
  );
}

/**
 * Load the last-used identity from device-local storage, or null when there is
 * none, storage is unavailable, or the stored value fails validation. Never
 * throws: any storage or parse error resolves to null (the caller then falls
 * back to an empty name + the default variant).
 */
export function loadIdentity(): StoredIdentity | null {
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
    const { nickname, variant } = record;
    if (typeof nickname !== 'string' || !isGuardianVariant(variant)) {
      return null;
    }

    return { nickname, variant };
  } catch {
    // Storage unavailable / disabled, quota, or malformed JSON - treat as "none".
    return null;
  }
}

/**
 * Remember the given name + Guardian variant for next time (device-local only).
 * Called on a SUCCESSFUL create / join, so a returning player is pre-filled.
 * Silently no-ops if storage is unavailable - persistence here is a convenience,
 * never a requirement, and must never break the create/join it follows.
 */
export function saveIdentity(nickname: string, variant: GuardianVariant): void {
  try {
    const value: StoredIdentity = { nickname, variant };
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
  } catch {
    // Ignore: a failed write just means we do not pre-fill next time.
  }
}
