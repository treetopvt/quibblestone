// ----------------------------------------------------------------------------
//  SettingDefinition + SettingBounds - the CODE-SIDE declaration of one runtime
//  settings key (control-plane/01, issue #197). A definition is the key's identity,
//  type, code default, and (for numeric keys) the guard rails an operator PUT can
//  never step outside. Definitions live in the static SettingsCatalog list; story 02
//  and story 03 APPEND their keys to that list (same file, serialized by wave).
//
//  THE TWO SAFETY-RAIL FIELDS (2026-07-08 adversarial-review finding, ADR 0003
//  "The control plane cannot disable its own safety rails"):
//    - Bounds  : numeric keys only. A PUT value that type-parses but falls outside
//                [Min, Max] is REJECTED (AC-08) - a type check alone is not enough
//                (it would let an operator uncap AI spend or zero a tale TTL).
//    - RequiresConfirmation : set true on *.enabled kill switches (story 02) and on
//                ai.spend.monthlyCeilingUsd (story 03). A PUT to such a key without an
//                explicit confirm:true is rejected (AC-10) - a load-bearing flip can
//                never be an accidental one-field call.
//
//  CodeDefault is the typed default (bool / int / decimal / string), returned whenever
//  no override is stored (AC-01) or a stored override fails to parse (AC-07) - the same
//  "safe/known default when nothing is stored" posture as the Stripe-mode precedent.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Globalization;

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The inclusive numeric range a <see cref="SettingType.Int"/> / <see cref="SettingType.Decimal"/>
/// key may take (control-plane/01, AC-08). Both bounds are held as <see cref="decimal"/> so one
/// type covers integer and decimal keys; an <see cref="SettingType.Int"/> key's bounds are whole
/// numbers. <c>null</c> on a <see cref="SettingDefinition"/> means "no declared range" and is only
/// valid for <see cref="SettingType.Bool"/> / <see cref="SettingType.String"/> keys.
/// </summary>
/// <param name="Min">The smallest value a PUT may set (inclusive).</param>
/// <param name="Max">The largest value a PUT may set (inclusive).</param>
public sealed record SettingBounds(decimal Min, decimal Max)
{
    /// <summary>
    /// True when <paramref name="value"/> is within <c>[Min, Max]</c> (inclusive). Called on a
    /// value that has ALREADY parsed against its declared type, so a bad-range value is rejected
    /// on PUT before any write or log row (AC-08).
    /// </summary>
    public bool Contains(decimal value) => value >= Min && value <= Max;

    /// <summary>A short operator-facing description of the range, e.g. <c>"1 to 100"</c>.</summary>
    public string Describe() =>
        $"{Min.ToString(CultureInfo.InvariantCulture)} to {Max.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The code-side declaration of one runtime settings key (control-plane/01). Immutable;
/// registered as a static entry in <see cref="SettingsCatalog"/>. The catalog is the single
/// source of truth for a key's type, default, and guard rails - never a per-feature one-off.
/// </summary>
/// <param name="Key">The dotted, namespaced key (e.g. <c>moderation.tale.autoHideThreshold</c>), stable and used as the store RowKey.</param>
/// <param name="Type">The value shape (<see cref="SettingType"/>). A typed getter that does not match this is a coding bug.</param>
/// <param name="CodeDefault">The typed default value (bool / int / decimal / string), returned when no override is stored or a stored one fails to parse.</param>
/// <param name="Description">A short operator-facing description of what the knob does.</param>
/// <param name="Bounds">The inclusive numeric range for a numeric key (AC-08), or null for a Bool / String key with no natural range.</param>
/// <param name="RequiresConfirmation">True to require an explicit <c>confirm:true</c> on a PUT (AC-10) - set on kill switches and the spend ceiling.</param>
public sealed record SettingDefinition(
    string Key,
    SettingType Type,
    object CodeDefault,
    string Description,
    SettingBounds? Bounds = null,
    bool RequiresConfirmation = false)
{
    /// <summary>True when this is a numeric key (<see cref="SettingType.Int"/> / <see cref="SettingType.Decimal"/>).</summary>
    public bool IsNumeric => Type is SettingType.Int or SettingType.Decimal;
}
