// ----------------------------------------------------------------------------
//  ProductCatalog - the "which capability key(s) does this product grant, and how
//  is it billed" table (billing-entitlements/04, issue #73; made mode-aware in /06).
//  This is the small, explicit map the story's Technical Notes call for: its VALUE is
//  a LIST of capability keys (one key for a pack, the whole bundle for the family
//  plan - ADR 0002 Decision C), not always exactly one. Adding a new pack is a new
//  entry here plus a price id in config - "config flip, not a refactor".
//
//  MODE-AWARE PRICE IDS (billing-entitlements/06): Stripe price ids are mode-specific
//  (a test-mode price id is not a live-mode one), so the catalog no longer bakes a
//  single price id at construction. The product DEFINITIONS (id, copy, mode,
//  capability keys) are fixed; the resolved price id + purchasability are computed
//  against the ACTIVE mode's credential set (StripeModeConfig) passed in by the
//  caller (BillingController resolves the active mode first). A product with no
//  configured price id FOR THE ACTIVE MODE is simply not purchasable yet.
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
/// configured for the active mode - then it is not yet purchasable). Built by
/// <see cref="IProductCatalog"/> against the active mode's credentials.
/// </summary>
/// <param name="ProductId">Stable product id used by the client + checkout (e.g. "family-plan", "pack.spooky", "tip").</param>
/// <param name="DisplayName">Kid-safe, plain display name for the paywall / tip UI.</param>
/// <param name="Description">A short, no-dark-patterns description (plain pricing/value, AC-04).</param>
/// <param name="Mode">One-time payment or recurring subscription (AC-01).</param>
/// <param name="CapabilityKeys">The catalog capability keys granted on purchase; EMPTY for a tip (story 02 AC-02).</param>
/// <param name="PriceId">The Stripe price id for the ACTIVE mode (from config); empty => not purchasable yet.</param>
public sealed record BillingProduct(
    string ProductId,
    string DisplayName,
    string Description,
    CheckoutMode Mode,
    IReadOnlyList<string> CapabilityKeys,
    string PriceId)
{
    /// <summary>True when a Stripe price id is configured for this product in the active mode (it can be bought).</summary>
    public bool IsPurchasable => !string.IsNullOrWhiteSpace(PriceId);
}

/// <summary>
/// The product-to-capability map (billing-entitlements/04). Resolves a product id to
/// its capability bundle + billing mode + the ACTIVE mode's price id, and lists the
/// purchasable paywall products. The tip product is resolvable by id but excluded from
/// the paywall list (it has its own goodwill surface, story 02). Callers pass the
/// active mode's credentials (billing-entitlements/06) so price ids track the mode.
/// </summary>
public interface IProductCatalog
{
    /// <summary>The stable product id of the goodwill tip (story 02) - grants no capability.</summary>
    string TipProductId { get; }

    /// <summary>The products shown on the paywall (story 04), with price ids resolved for <paramref name="modeConfig"/>. Excludes the tip.</summary>
    IReadOnlyList<BillingProduct> PaywallProducts(StripeModeConfig modeConfig);

    /// <summary>Resolves any known product (paywall products + the tip) by id with the price id for <paramref name="modeConfig"/>, or null if unknown.</summary>
    BillingProduct? Resolve(string productId, StripeModeConfig modeConfig);
}

/// <summary>
/// The default <see cref="IProductCatalog"/> (billing-entitlements/04, /06): product
/// DEFINITIONS are fixed in code (id, copy, mode, capability bundle); the price id is
/// resolved per call from the active mode's <see cref="StripeModeConfig.PriceIds"/>. A
/// singleton - the definitions are fixed after construction, and it holds no mode state.
/// </summary>
public sealed class ProductCatalog : IProductCatalog
{
    // A product's mode-independent definition: everything except the price id.
    private sealed record ProductDefinition(
        string ProductId,
        string DisplayName,
        string Description,
        CheckoutMode Mode,
        IReadOnlyList<string> CapabilityKeys);

    /// <inheritdoc />
    public string TipProductId => "tip";

    private readonly List<ProductDefinition> _paywall;
    private readonly Dictionary<string, ProductDefinition> _byId;

    /// <summary>Builds the fixed product definitions (no price ids - those resolve per active mode).</summary>
    public ProductCatalog()
    {
        // The family plan: the FULL paid-tier bundle (ADR 0002 Decision C). The ai.*
        // keys are deliberately NOT bundled yet - those features do not exist (story 04
        // Out of Scope); they join this list when they ship, a one-line change here.
        var familyPlan = new ProductDefinition(
            ProductId: "family-plan",
            DisplayName: "QuibbleStone Family Plan",
            Description: "Unlock the full library, remote play across houses, and bigger groups for the whole family.",
            Mode: CheckoutMode.Subscription,
            CapabilityKeys: [EntitlementCatalog.LibraryFull, EntitlementCatalog.PlayRemote, EntitlementCatalog.PlayLargeGroup]);

        // One concrete example add-on pack (story 04 ships one path, not a storefront).
        // A new pack is another entry like this + a configured price id.
        var spookyPack = new ProductDefinition(
            ProductId: "pack.spooky",
            DisplayName: "Spooky Pack",
            Description: "A themed set of spooky fill-in-the-blank tales to add to your library.",
            Mode: CheckoutMode.Payment,
            CapabilityKeys: [EntitlementCatalog.Pack("spooky")]);

        // The goodwill tip (story 02): a one-time payment that grants NOTHING (empty
        // capability list) - entitlement-neutral by design (story 02 AC-02).
        var tip = new ProductDefinition(
            ProductId: TipProductId,
            DisplayName: "Buy the Guardians a coffee",
            Description: "A small one-time thank-you. It unlocks nothing - just our gratitude.",
            Mode: CheckoutMode.Payment,
            CapabilityKeys: []);

        _paywall = [familyPlan, spookyPack];
        _byId = new Dictionary<string, ProductDefinition>(StringComparer.Ordinal)
        {
            [familyPlan.ProductId] = familyPlan,
            [spookyPack.ProductId] = spookyPack,
            [tip.ProductId] = tip,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<BillingProduct> PaywallProducts(StripeModeConfig modeConfig) =>
        _paywall.Select(d => ToProduct(d, modeConfig)).ToList();

    /// <inheritdoc />
    public BillingProduct? Resolve(string productId, StripeModeConfig modeConfig) =>
        _byId.TryGetValue(productId, out var definition) ? ToProduct(definition, modeConfig) : null;

    // Project a definition to a product, resolving the price id from the active mode's config.
    private static BillingProduct ToProduct(ProductDefinition d, StripeModeConfig modeConfig)
    {
        var priceId = modeConfig.PriceIds.TryGetValue(d.ProductId, out var id) ? id : string.Empty;
        return new BillingProduct(d.ProductId, d.DisplayName, d.Description, d.Mode, d.CapabilityKeys, priceId);
    }
}
