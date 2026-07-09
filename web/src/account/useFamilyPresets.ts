// ----------------------------------------------------------------------------
//  useFamilyPresets - the join-flow's read of "what one-tap seat presets can this
//  device offer?" (accounts-identity/08, issue #228).
//
//  The thin data hook behind the Join / HostSetup preset picker. It asks the shared
//  useFamilyCredential() helper whether THIS device holds a family-resolving
//  credential; if so it fetches that family's presets and returns them; if not it
//  returns an EMPTY list and fetches nothing (AC-06's degraded-but-shippable path -
//  no picker on a device with no family credential). The credential decision lives
//  in useFamilyCredential (story 09 extends it to a device-link token); this hook is
//  the same regardless of which credential type resolves the family.
//
//  DELIBERATELY READ-ONLY + PRESENTATION-ONLY: this hook never writes a preset (the
//  manager on the Account page owns create/edit/delete) and never touches the hub or
//  any room / player state. Selecting a preset in the picker fills the SAME
//  display-name / variant fields the manual path uses (AC-03) - this hook only
//  supplies the list to choose from.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { useFamilyCredential } from './useFamilyCredential';
import { fetchPresets, type SeatPreset } from './seatPresetsClient';

/** What the join-flow picker needs: the family's presets (empty when none / no credential). */
export interface FamilyPresets {
  /** The saved presets to offer as one-tap chips, or [] when this device holds no family credential. */
  presets: SeatPreset[];
}

/**
 * Returns the family's seat presets for the join-flow picker. On a device with a
 * family credential it fetches them once (re-fetching if the credential changes); on
 * a device with none it returns an empty list without any request (AC-06). A
 * transport failure resolves to an empty list too, so the picker simply does not
 * appear rather than showing an error in the join flow.
 */
export function useFamilyPresets(): FamilyPresets {
  const credential = useFamilyCredential();
  const [presets, setPresets] = useState<SeatPreset[]>([]);

  useEffect(() => {
    // No family credential on this device -> no picker, no fetch (AC-06). Clear any
    // presets left over from a prior signed-in state on the same mount.
    if (!credential) {
      setPresets([]);
      return;
    }

    let active = true;
    void fetchPresets(credential).then((result) => {
      if (!active) return;
      // Only 'ok' yields presets; 'signed-out' / 'error' leave the picker empty so a
      // hiccup never blocks the manual join path.
      setPresets(result.status === 'ok' ? result.presets : []);
    });
    return () => {
      active = false;
    };
  }, [credential]);

  return { presets };
}
