// ----------------------------------------------------------------------------
//  ProductCatalog - the "which capability key(s) does this product grant, and how
//  is it billed" table (billing-entitlements/04, issue #73). This is the small,
//  explicit map the story's Technical Notes call for: its VALUE is a LIST of
//  capability keys (one key for a pack, the whole bundle for the family plan - ADR
//  0002 Decision C), not always exactly one. Adding a new pack is a new entry here
//  plus a price id in config - "config flip, not a refactor" (billing-01's promise).
//
//  PRICE IDS come from StripeOptions.PriceIds (Key Vault / config, keyed by product
//  id) - never committed. When a product has no configured price id (local dev / CI /
//  a fresh clone), it is simply not purchasable yet; the catalog still knows its
//  capability mapping, so the seam is complete and testable with zero Stripe setup.
//
//  The TIP product (story 02) lives here too, with an EMPTY capability list - the
//  webhook grants nothing for it (story 02 AC-02), so the tip jar rides the SAME
//  checkout plumbing without a special case.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// A purchasable product: its stable id, display copy, billing mode, the capability
/// keys it grants (empty for a tip), and the resolved Stripe price id (empty when not
/// configured - then it is not yet purchasable). Built by <see cref="IProductCatalog"/>.
/// </summary>
/// <param name="ProductId">Stable product id used by the client + checkout (e.g. "family-plan", "pack.spooky", "tip").</param>
/// <param name="DisplayName">Kid-safe, plain display name for the paywall / tip UI.</param>
/// <param name="Description">A short, no-dark-patterns description (plain pricing/value, AC-04).</param>
/// <param name="Mode">One-time payment or recurring subscription (AC-01).</param>
/// <param name="CapabilityKeys">The catalog capability keys granted on purchase; EMPTY for a tip (story 02 AC-02).</param>
/// <param name="PriceId">The Stripe price id (from config); empty => not purchasable yet.</param>
public sealed record BillingProduct(
    string ProductId,
    string DisplayName,
    string Description,
    CheckoutMode Mode,
    IReadOnlyList<string> CapabilityKeys,
    string PriceId)
{
    /// <summary>True when a Stripe price id is configured for this product (it can be bought).</summary>
    public bool IsPurchasable => !string.IsNullOrWhiteSpace(PriceId);
}

/// <summary>
/// The product-to-capability map (billing-entitlements/04). Resolves a product id to
/// its capability bundle + billing mode + price id, and lists the purchasable
/// paywall products. The tip product is resolvable by id but excluded from the
/// paywall list (it has its own goodwill surface, story 02).
/// </summary>
public interface IProductCatalog
{
    /// <summary>The stable product id of the goodwill tip (story 02) - grants no capability.</summary>
    string TipProductId { get; }

    /// <summary>The products shown on the paywall (story 04): the family plan + any add-on packs. Excludes the tip.</summary>
    IReadOnlyList<BillingProduct> PaywallProducts { get; }

    /// <summary>Resolves any known product (paywall products + the tip) by id, or null if unknown.</summary>
    BillingProduct? Resolve(string productId);
}

/// <summary>
/// The default <see cref="IProductCatalog"/> (billing-entitlements/04): products are
/// defined in code (id, copy, mode, capability bundle), price ids are pulled from
/// <see cref="StripeOptions.PriceIds"/> (config, per product id). A singleton - the
/// map is fixed after construction.
/// </summary>
public sealed class ProductCatalog : IProductCatalog
{
    /// <inheritdoc />
    public string TipProductId => "tip";

    private readonly Dictionary<string, BillingProduct> _byId;
    private readonly List<BillingProduct> _paywall;

    /// <summary>Builds the catalog, resolving each product's price id from the configured options.</summary>
    public ProductCatalog(StripeOptions options)
    {
        string PriceFor(string productId) =>
            options.PriceIds.TryGetValue(productId, out var priceId) ? priceId : string.Empty;

        // The family plan: the FULL paid-tier bundle (ADR 0002 Decision C). The ai.*
        // keys are deliberately NOT bundled yet - those features do not exist (story 04
        // Out of Scope); they join this list when they ship, a one-line change here.
        var familyPlan = new BillingProduct(
            ProductId: "family-plan",
            DisplayName: "QuibbleStone Family Plan",
            Description: "Unlock the full library, remote play across houses, and bigger groups for the whole family.",
            Mode: CheckoutMode.Subscription,
            CapabilityKeys: [EntitlementCatalog.LibraryFull, EntitlementCatalog.PlayRemote, EntitlementCatalog.PlayLargeGroup],
            PriceId: PriceFor("family-plan"));

        // One concrete example add-on pack (story 04 ships one path, not a storefront).
        // A new pack is another entry like this + a configured price id.
        var spookyPack = new BillingProduct(
            ProductId: "pack.spooky",
            DisplayName: "Spooky Pack",
            Description: "A themed set of spooky fill-in-the-blank tales to add to your library.",
            Mode: CheckoutMode.Payment,
            CapabilityKeys: [EntitlementCatalog.Pack("spooky")],
            PriceId: PriceFor("pack.spooky"));

        // The goodwill tip (story 02): a one-time payment that grants NOTHING (empty
        // capability list) - entitlement-neutral by design (story 02 AC-02).
        var tip = new BillingProduct(
            ProductId: TipProductId,
            DisplayName: "Buy the Guardians a coffee",
            Description: "A small one-time thank-you. It unlocks nothing - just our gratitude.",
            Mode: CheckoutMode.Payment,
            CapabilityKeys: [],
            PriceId: PriceFor(TipProductId));

        _paywall = [familyPlan, spookyPack];
        _byId = new Dictionary<string, BillingProduct>(StringComparer.Ordinal)
        {
            [familyPlan.ProductId] = familyPlan,
            [spookyPack.ProductId] = spookyPack,
            [tip.ProductId] = tip,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<BillingProduct> PaywallProducts => _paywall;

    /// <inheritdoc />
    public BillingProduct? Resolve(string productId) =>
        _byId.TryGetValue(productId, out var product) ? product : null;
}
