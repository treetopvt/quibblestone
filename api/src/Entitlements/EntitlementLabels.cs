// ----------------------------------------------------------------------------
//  EntitlementLabels - the ONE place a capability key maps to a friendly, plain-
//  language display name (billing-entitlements/05, issue #74). The restore/manage
//  view lists "what is unlocked" in human terms ("Full Library", "Spooky Pack"), so
//  the key -> label mapping lives here once - adding a new pack does not touch the
//  view's rendering logic (story 05 Technical Notes).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Globalization;

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// Maps an <see cref="EntitlementCatalog"/> capability key to a friendly display name
/// for the restore/manage view (billing-entitlements/05). Unknown keys degrade to a
/// readable form of the key itself rather than throwing.
/// </summary>
public static class EntitlementLabels
{
    /// <summary>
    /// A plain-language label for a capability key: the fixed catalog keys get curated
    /// names; a <c>pack.&lt;id&gt;</c> key becomes "&lt;Id&gt; Pack" (e.g. "pack.spooky"
    /// -&gt; "Spooky Pack"); anything else falls back to the raw key.
    /// </summary>
    /// <param name="capabilityKey">A catalog capability key.</param>
    /// <returns>A friendly display name.</returns>
    public static string LabelFor(string capabilityKey) => capabilityKey switch
    {
        EntitlementCatalog.LibraryFull => "Full Library",
        EntitlementCatalog.PlayRemote => "Remote Play",
        EntitlementCatalog.PlayLargeGroup => "Large Groups",
        EntitlementCatalog.AiOnDemand => "AI Word Bank",
        _ when capabilityKey.StartsWith(EntitlementCatalog.PackPrefix, StringComparison.Ordinal) => PackLabel(capabilityKey),
        _ => capabilityKey,
    };

    // "pack.spooky" -> "Spooky Pack". The id is title-cased (invariant) for display.
    private static string PackLabel(string capabilityKey)
    {
        var id = capabilityKey[EntitlementCatalog.PackPrefix.Length..];
        if (id.Length == 0)
        {
            return "Pack";
        }
        var titled = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));
        return $"{titled} Pack";
    }
}
