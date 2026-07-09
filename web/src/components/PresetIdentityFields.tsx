// ----------------------------------------------------------------------------
//  PresetIdentityFields - the shared identity controls (PlayerIdentityFields) with
//  a one-tap kid seat-preset picker layered ABOVE them (accounts-identity/08, #228).
//
//  This is the thin wrapper the story calls for: Join and HostSetup render it in
//  place of the bare <PlayerIdentityFields>, so BOTH the joiner-names-itself and the
//  host-names-itself screens get the same preset picker with zero duplicated wiring.
//  It:
//    1. asks useFamilyPresets() what presets this device can offer (empty on a device
//       with no family credential - AC-06, no picker there);
//    2. renders <SeatPresetPicker> above the manual fields; and
//    3. on a chip tap, fills the SAME controlled name + variant the manual path uses.
//
//  THE HARD BOUNDARY (AC-03): tapping a preset is EXACTLY typing that nickname and
//  picking that Guardian by hand. handleSelect below calls the CALLER'S existing
//  onNicknameChange / onVariantChange - the very handlers the manual TextField and
//  avatar grid already call - and does nothing else. There is no new submit path and
//  no "preset" marker: the caller then submits through its SAME CreateRoom / JoinRoom
//  invoke, so the server cannot tell a preset join from a manual one. The nickname is
//  capped at MAX_NAME_LENGTH here exactly as the manual field caps typing, and is
//  still safety-filtered SERVER-SIDE on the hub like any name (the picker never
//  pre-approves anything, AC-04).
//
//  Purely presentational over the caller's form: it holds no form state (the caller
//  owns nickname + variant, same as PlayerIdentityFields), only the read-only preset
//  list from the hook.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useFamilyPresets } from '../account/useFamilyPresets';
import type { SeatPreset } from '../account/seatPresetsClient';
import { PlayerIdentityFields, MAX_NAME_LENGTH } from './PlayerIdentityFields';
import type { PlayerIdentityFieldsProps } from './PlayerIdentityFields';
import { SeatPresetPicker } from './SeatPresetPicker';

/**
 * The shared display-name + Guardian controls with the seat-preset picker above.
 * Takes the SAME props as {@link PlayerIdentityFields} (the caller owns nickname +
 * variant) and, on a preset tap, fills those controlled fields via the caller's own
 * change handlers - it fills, it never submits (AC-03).
 */
export function PresetIdentityFields(props: PlayerIdentityFieldsProps) {
  const { presets } = useFamilyPresets();
  const { onNicknameChange, onVariantChange } = props;

  // Tapping a preset is identical to typing that name + picking that Guardian: fill
  // the SAME fields via the caller's handlers (nickname capped exactly as the manual
  // field caps typing), then let the caller's UNCHANGED submit path take over (AC-03).
  const handleSelect = (preset: SeatPreset) => {
    onNicknameChange(preset.nickname.slice(0, MAX_NAME_LENGTH));
    onVariantChange(preset.variant);
  };

  return (
    <>
      <SeatPresetPicker presets={presets} onSelect={handleSelect} />
      <PlayerIdentityFields {...props} />
    </>
  );
}
