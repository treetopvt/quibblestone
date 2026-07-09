// ----------------------------------------------------------------------------
//  BillingController - the purchaser-facing REST surface that STARTS a checkout
//  (billing-entitlements/04 gated purchase + billing-entitlements/02 tip jar). It is
//  request/response (the web paywall / tip jar call it, then redirect the browser to
//  the returned Stripe-hosted URL), NOT a hub method - so it lives with the other
//  controllers and never touches GameHub or any room/player state.
//
//  WHAT IT DOES:
//    GET  /api/billing/products  -> the paywall products (+ whether billing is on)
//    POST /api/billing/checkout  -> start a gated-purchase checkout for a product id
//    POST /api/billing/tip       -> start a goodwill tip checkout (grants nothing)
//
//  THE CAPABILITY MAP LIVES SERVER-SIDE (billing-04): the client sends a product id,
//  never capability keys or a price - BillingController resolves the product via
//  IProductCatalog and passes its capability bundle + price + mode into
//  StripeCheckoutService. So the browser can never ask to be granted an arbitrary
//  capability; it can only buy a known product.
//
//  CONFIG-OFF: when Stripe is not configured, StripeCheckoutService is the disabled
//  no-op and every checkout returns { enabled: false } - the client shows a friendly
//  "not available yet" state (no error). Free play is completely unaffected.
//
//  CHILD SAFETY (story 02 AC-05): the tip's optional "message to the Guardians" free
//  text runs through the SAME server-side IContentSafetyFilter every nickname / blank
//  answer uses - never a second filter - BEFORE it is sent anywhere. A blocked
//  message stops the tip with a friendly note; QuibbleStone stores no message itself.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Controllers;

/// <summary>A paywall product for the client (billing-entitlements/04): id, copy, mode, and whether it can be bought.</summary>
/// <param name="ProductId">Stable id the client sends back to /checkout.</param>
/// <param name="DisplayName">Kid-safe display name.</param>
/// <param name="Description">Plain, no-dark-patterns description.</param>
/// <param name="Mode">"payment" or "subscription".</param>
/// <param name="Purchasable">False when no price id is configured yet (shown but not buyable).</param>
public sealed record ProductView(string ProductId, string DisplayName, string Description, string Mode, bool Purchasable);

/// <summary>Response for GET /api/billing/products: whether billing is on + the paywall products.</summary>
public sealed record ProductsResult(bool Enabled, IReadOnlyList<ProductView> Products);

/// <summary>Request to start a gated-purchase checkout.</summary>
/// <param name="ProductId">The product id from GET /api/billing/products.</param>
/// <param name="PurchaserEmail">Optional purchaser email (prefills checkout; keys the resulting grant, AC-06).</param>
public sealed record CheckoutRequestBody(string? ProductId, string? PurchaserEmail);

/// <summary>Request to start a goodwill tip checkout (story 02).</summary>
/// <param name="Message">Optional "message to the Guardians" free text - safety-filtered before use (AC-05).</param>
public sealed record TipRequestBody(string? Message);

/// <summary>Response for a checkout/tip start: whether billing is enabled and the URL to redirect to.</summary>
/// <param name="Enabled">False when billing is not configured (show a friendly "not available yet").</param>
/// <param name="Url">The Stripe-hosted checkout URL to redirect to (present on success).</param>
/// <param name="Message">A friendly message for a not-enabled or blocked outcome (e.g. a filtered tip message).</param>
public sealed record CheckoutStartResult(bool Enabled, string? Url, string? Message);

[ApiController]
[Route("api/billing")]
public sealed class BillingController : ControllerBase
{
    // Where Stripe returns the browser after checkout. Each path must be the ROUTE
    // whose page reads the corresponding query param: /get-more (GetMore reads
    // ?purchase) and /support (Support reads ?tip). The client base comes from config
    // so this is not hardcoded to one origin. (Copilot review: the tip paths formerly
    // pointed at "/" (Home), where nothing reads ?tip - so the thank-you / cancel state
    // in Support never showed. They must target /support.)
    private const string SuccessPath = "/get-more?purchase=success";
    private const string CancelPath = "/get-more?purchase=cancel";
    private const string TipSuccessPath = "/support?tip=success";
    private const string TipCancelPath = "/support?tip=cancel";

    private readonly IStripeCheckoutService _checkout;
    private readonly IProductCatalog _catalog;
    private readonly IContentSafetyFilter _safety;
    private readonly IActiveStripeContext _context;
    private readonly StripeOptions _options;

    public BillingController(
        IStripeCheckoutService checkout,
        IProductCatalog catalog,
        IContentSafetyFilter safety,
        IActiveStripeContext context,
        StripeOptions options)
    {
        _checkout = checkout;
        _catalog = catalog;
        _safety = safety;
        _context = context;
        _options = options;
    }

    /// <summary>GET /api/billing/products - the paywall products and whether billing is on.</summary>
    [HttpGet("products")]
    public async Task<IActionResult> Products(CancellationToken ct)
    {
        // Price ids + purchasability are resolved against the ACTIVE mode
        // (billing-entitlements/06) - a product with no price id in the active mode is
        // shown but not buyable.
        var config = await _context.GetActiveConfigAsync(ct);
        var products = _catalog.PaywallProducts(config)
            .Select(p => new ProductView(
                p.ProductId,
                p.DisplayName,
                p.Description,
                p.Mode == CheckoutMode.Subscription ? "subscription" : "payment",
                Purchasable: _checkout.IsEnabled && p.IsPurchasable))
            .ToList();
        return Ok(new ProductsResult(Enabled: _checkout.IsEnabled, Products: products));
    }

    /// <summary>
    /// POST /api/billing/checkout - start a gated-purchase checkout for a product id.
    /// Resolves the product's capability bundle server-side (billing-04) and hands it to
    /// StripeCheckoutService. Returns { enabled:false } when billing is off. The purchaser
    /// account is created by the webhook on completion (AC-06) - no forced sign-up here.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequestBody? request, CancellationToken ct)
    {
        var config = await _context.GetActiveConfigAsync(ct);
        var product = string.IsNullOrWhiteSpace(request?.ProductId) ? null : _catalog.Resolve(request.ProductId, config);
        // A tip must go through the tip endpoint (it safety-filters the message); the
        // paywall checkout only sells paywall products.
        if (product is null || string.Equals(product.ProductId, _catalog.TipProductId, StringComparison.Ordinal))
        {
            return NotFound(new CheckoutStartResult(_checkout.IsEnabled, Url: null, Message: "That item is not available."));
        }

        var result = await StartCheckoutAsync(product, request?.PurchaserEmail, SuccessPath, CancelPath, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/billing/tip - start a goodwill tip checkout (story 02). Safety-filters
    /// any message first (AC-05), then creates a one-time checkout for the tip product,
    /// which grants NOTHING (AC-02). No sign-in / account is required (AC-03).
    /// </summary>
    [HttpPost("tip")]
    public async Task<IActionResult> Tip([FromBody] TipRequestBody? request, CancellationToken ct)
    {
        // Child safety (AC-05): a message, if offered, runs through the SAME filter as a
        // nickname / blank answer before it goes anywhere. Blocked -> stop with a note.
        var message = request?.Message?.Trim();
        if (!string.IsNullOrEmpty(message))
        {
            var verdict = await _safety.CheckAsync(message, ct);
            if (!verdict.IsAllowed)
            {
                return Ok(new CheckoutStartResult(_checkout.IsEnabled, Url: null,
                    Message: verdict.Message ?? "Let's keep that message friendly - please try different words."));
            }
        }

        var config = await _context.GetActiveConfigAsync(ct);
        var tip = _catalog.Resolve(_catalog.TipProductId, config);
        if (tip is null)
        {
            return Ok(new CheckoutStartResult(Enabled: false, Url: null, Message: null));
        }

        // The tip grants nothing (empty capability keys) - it rides the same plumbing.
        var result = await StartCheckoutAsync(tip, purchaserEmail: null, TipSuccessPath, TipCancelPath, ct);
        return Ok(result);
    }

    // Shared: build the CheckoutRequest for a product and start the Stripe session.
    private async Task<CheckoutStartResult> StartCheckoutAsync(
        BillingProduct product, string? purchaserEmail, string successPath, string cancelPath, CancellationToken ct)
    {
        if (!_checkout.IsEnabled || !product.IsPurchasable)
        {
            // Billing off (or this product has no configured price) - a clean "not
            // available yet" the client renders as a friendly note, never an error.
            return new CheckoutStartResult(Enabled: false, Url: null,
                Message: "Purchases are not available just now - free play is always on.");
        }

        var checkoutRequest = new CheckoutRequest(
            Mode: product.Mode,
            PriceId: product.PriceId,
            SuccessUrl: _options.ClientBaseUrl.TrimEnd('/') + successPath,
            CancelUrl: _options.ClientBaseUrl.TrimEnd('/') + cancelPath,
            CapabilityKeys: product.CapabilityKeys,
            PurchaserEmail: purchaserEmail,
            // billing-entitlements/08: carry the product id so the grant records its
            // PlanId (a one-line addition - the product is already resolved here).
            ProductId: product.ProductId);

        var session = await _checkout.CreateCheckoutSessionAsync(checkoutRequest, ct);
        return new CheckoutStartResult(session.Enabled, session.Url, Message: null);
    }
}
