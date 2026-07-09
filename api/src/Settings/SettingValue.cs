// ----------------------------------------------------------------------------
//  SettingValue - the ONE place a settings value crosses the typed <-> wire-string
//  boundary (control-plane/01, issue #197). The store persists a value as a single
//  string column (mirroring TableStorageActiveStripeModeStore's Mode column); this
//  helper formats a typed value INTO that wire form and parses it back OUT against a
//  key's declared SettingType. Culture-invariant throughout, so a decimal written on
//  one host reads identically on another.
//
//  SAFE PARSE (AC-07): TryParse NEVER throws - a value that does not match its declared
//  type returns false, and the caller (the service getters / GetAllAsync) degrades to
//  the code default, exactly like the Stripe-mode store's unparseable handling. The PUT
//  path uses the SAME TryParse to reject a malformed value with 400 before any write.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Globalization;

namespace QuibbleStone.Api.Settings;

/// <summary>
/// Typed &lt;-&gt; wire-string conversion for settings values (control-plane/01). Culture-
/// invariant; <see cref="TryParse"/> never throws (AC-07). The single codec both the write
/// path (validate a PUT) and the read path (resolve an override) share, so they can never
/// disagree on what a stored string means.
/// </summary>
public static class SettingValue
{
    /// <summary>
    /// Formats a typed <paramref name="value"/> into its wire-string form for the given
    /// <paramref name="type"/>. Booleans are lowercased (<c>"true"</c> / <c>"false"</c>);
    /// numerics use the invariant culture; strings pass through. Throws
    /// <see cref="InvalidCastException"/> if the runtime value does not match the declared
    /// type (a coding bug - the catalog default and the parsed value are always in step).
    /// </summary>
    public static string Format(SettingType type, object value) => type switch
    {
        SettingType.Bool => (bool)value ? "true" : "false",
        SettingType.Int => ((int)value).ToString(CultureInfo.InvariantCulture),
        SettingType.Decimal => ((decimal)value).ToString(CultureInfo.InvariantCulture),
        SettingType.String => (string)value,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown setting type."),
    };

    /// <summary>
    /// Parses <paramref name="raw"/> against <paramref name="type"/> into a boxed typed value
    /// (<see cref="bool"/> / <see cref="int"/> / <see cref="decimal"/> / <see cref="string"/>).
    /// Returns false (never throws) on any mismatch, so a drifted / hand-edited row degrades to
    /// the code default (AC-07) and a malformed PUT is a 400 (AC-08's type gate). A String key
    /// always parses (its wire form IS the value).
    /// </summary>
    public static bool TryParse(SettingType type, string? raw, out object value)
    {
        switch (type)
        {
            case SettingType.Bool:
                if (bool.TryParse(raw, out var b))
                {
                    value = b;
                    return true;
                }
                break;

            case SettingType.Int:
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    value = i;
                    return true;
                }
                break;

            case SettingType.Decimal:
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                {
                    value = d;
                    return true;
                }
                break;

            case SettingType.String:
                // A String key's wire form IS its value - always "parses" (an absent body is
                // guarded upstream at the controller; here an empty string is a legal value).
                value = raw ?? string.Empty;
                return true;
        }

        value = null!;
        return false;
    }

    /// <summary>
    /// Projects a parsed numeric value to <see cref="decimal"/> for a <see cref="SettingBounds"/>
    /// check (AC-08). Only valid for <see cref="SettingType.Int"/> / <see cref="SettingType.Decimal"/>
    /// values; a non-numeric type is a coding bug (bounds are only declared on numeric keys).
    /// </summary>
    public static decimal ToDecimal(SettingType type, object value) => type switch
    {
        SettingType.Int => (int)value,
        SettingType.Decimal => (decimal)value,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Bounds apply to numeric keys only."),
    };
}
