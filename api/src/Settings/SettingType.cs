// ----------------------------------------------------------------------------
//  SettingType - the closed set of value shapes a runtime settings key can hold
//  (control-plane/01, issue #197). Every SettingDefinition in the SettingsCatalog
//  declares EXACTLY one of these, and every typed getter on IRuntimeSettingsService
//  matches one of them: asking GetIntAsync for a key declared Bool is a CODING BUG
//  (the service throws), not a runtime branch - the catalog is the single source of
//  truth for a key's type.
//
//  WHY A CLOSED ENUM (not "store anything"): the store persists a value as its wire-
//  string form (one column, mirroring TableStorageActiveStripeModeStore's single Mode
//  column); the declared type is what parses that string back safely (SettingValue).
//  A missing / drifted / hand-edited row degrades to the code default rather than
//  throwing (AC-07) - the same posture as the Stripe-mode store's unparseable handling.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The value shape a runtime settings key holds (control-plane/01). Numeric kinds
/// (<see cref="Int"/> / <see cref="Decimal"/>) are the only ones that may carry a
/// <see cref="SettingBounds"/>; <see cref="Bool"/> / <see cref="String"/> have no
/// natural numeric range (a boolean kill switch is gated by
/// <see cref="SettingDefinition.RequiresConfirmation"/> instead, AC-10).
/// </summary>
public enum SettingType
{
    /// <summary>A boolean flag (e.g. a <c>*.enabled</c> kill switch). Confirmation-gated when load-bearing.</summary>
    Bool,

    /// <summary>A 32-bit integer knob (e.g. a threshold or a per-minute permit). Bounds-checked on PUT.</summary>
    Int,

    /// <summary>A decimal knob (e.g. a monetary ceiling). Bounds-checked on PUT.</summary>
    Decimal,

    /// <summary>A free-form string knob. No numeric bounds; still catalog-declared.</summary>
    String,
}
