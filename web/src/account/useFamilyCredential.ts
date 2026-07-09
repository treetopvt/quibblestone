// ----------------------------------------------------------------------------
//  useFamilyCredential - "does THIS device hold a family-resolving credential?"
//  (accounts-identity/08, issue #228).
//
//  The ONE small shared helper the seat-preset join picker asks before it fetches
//  or shows anything. It returns the credential this device can present to the
//  account-plane preset endpoints to resolve a family, or null when the device
//  holds none (in which case the picker renders nothing - AC-06's degraded-but-
//  shippable path: no picker on a device with no family credential).
//
//  WHY A SEPARATE HELPER (forward-compatible with story 09): today the ONLY family-
//  resolving credential is the signed-in parent's PurchaserSession credential
//  (accounts-identity/03) - so on this story's ships, the preset picker appears ONLY
//  on the signed-in parent's own device (AC-06). accounts-identity/09 adds a SECOND
//  credential type (a family device-link token a kid's own tablet redeems once); when
//  it lands it only has to extend THIS helper to also return that token, and the
//  picker component + the presets hook never change. Keeping the "which credential"
//  decision here (not inside the picker) is exactly that seam.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { usePurchaserSession } from './PurchaserSession';

/**
 * Returns the family-resolving credential this device holds, or null when it holds
 * none. Today that is the signed-in parent's purchaser credential
 * (accounts-identity/03); accounts-identity/09 will extend this to also consider a
 * redeemed family device-link token, with no change to the picker that consumes it.
 */
export function useFamilyCredential(): string | null {
  // The in-memory purchaser session (never persisted) - a signed-in parent's own
  // device. On a device that never signed in, `credential` is null and the picker
  // shows nothing (AC-06).
  const { credential } = usePurchaserSession();
  return credential;
}
