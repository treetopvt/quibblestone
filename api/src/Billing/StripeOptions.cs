// ----------------------------------------------------------------------------
//  StripeOptions - the bound configuration for the Stripe billing seam
//  (billing-entitlements/03, issue #72; reshaped mode-aware in /06).
//
//  MODE-AWARE (billing-entitlements/06): the app holds BOTH a Live and a Test
//  credential set at once (Stripe keeps them completely separate - separate keys,
//  webhooks, and price ids), and a runtime flag (IActiveStripeModeStore) selects
//  which is ACTIVE. Each mode's credentials live under its own config sub-section:
//    Stripe:Live:SecretKey / Stripe:Live:WebhookSigningSecret / Stripe:Live:PriceIds:*
//    Stripe:Test:SecretKey / Stripe:Test:WebhookSigningSecret / Stripe:Test:PriceIds:*
//
//  BACKWARD COMPATIBILITY: the ORIGINAL flat fields (Stripe:SecretKey etc., story
//  03's single-mode shape) are still bound and act as the FALLBACK for any mode
//  whose own sub-section is not configured (see ForMode). So an environment wired
//  the old way (a single flat secret key + price ids) keeps working unchanged - it
//  resolves as BOTH modes until a per-mode section is supplied. New environments
//  set the per-mode sections and leave the flat fields empty.
//
//  SECRETS (AC-01): every SecretKey / WebhookSigningSecret (flat or per-mode) is a
//  SECRET - supplied per-environment from Azure Key Vault, NEVER a committed literal
//  and NEVER a VITE_* var. PublishableKey is NOT a secret but IS mode-specific.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>
/// One Stripe mode's credential set (billing-entitlements/06): the secret key +
/// webhook signing secret (SECRETS, Key Vault-backed) and the non-secret,
/// mode-specific publishable key and price ids. Held twice by <see cref="StripeOptions"/>
/// (Live + Test). <see cref="IsConfigured"/> is true once a secret key is present.
/// </summary>
public sealed class StripeModeConfig
{
    /// <summary>The Stripe SECRET key for this mode (Key Vault-backed). Empty => this mode is not configured.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The Stripe webhook SIGNING secret for this mode (Key Vault-backed).</summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>The NON-secret publishable key for this mode (mode-specific; safe in the browser).</summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>Stripe price ids keyed by product id, for THIS mode (price ids are mode-specific).</summary>
    public Dictionary<string, string> PriceIds { get; set; } = new();

    /// <summary>True when a secret key is configured for this mode (a real checkout can run in it).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>
/// Bound from the "Stripe" configuration section (billing-entitlements/03, /06). Holds
/// the per-mode credential sets (<see cref="Live"/> / <see cref="Test"/>), the legacy
/// flat fields (fallback for an old single-mode wiring), and the mode-INDEPENDENT
/// fields (client base URL, dunning grace window).
/// </summary>
public sealed class StripeOptions
{
    /// <summary>The configuration section name these options bind from.</summary>
    public const string SectionName = "Stripe";

    // --- Per-mode credential sets (billing-entitlements/06) --------------------

    /// <summary>The LIVE mode credentials (real money). Empty => falls back to the flat fields (see <see cref="ForMode"/>).</summary>
    public StripeModeConfig Live { get; set; } = new();

    /// <summary>The TEST mode credentials (test cards only). Empty => falls back to the flat fields (see <see cref="ForMode"/>).</summary>
    public StripeModeConfig Test { get; set; } = new();

    // --- Legacy flat fields (story 03 single-mode shape; back-compat fallback) --

    /// <summary>LEGACY flat secret key (story 03). Fallback for any mode whose own sub-section is unset. Empty when per-mode config is used.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>LEGACY flat webhook signing secret (story 03). Fallback for any mode whose own sub-section is unset.</summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>LEGACY flat publishable key (story 03). Fallback for any mode whose own sub-section is unset.</summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>LEGACY flat price ids keyed by product id (story 03). Fallback for any mode whose own sub-section is unset.</summary>
    public Dictionary<string, string> PriceIds { get; set; } = new();

    // --- Mode-independent fields -----------------------------------------------

    /// <summary>
    /// The web app's base URL used to build the checkout success/cancel redirect URLs
    /// (billing-entitlements/04). Not a secret; NEVER a VITE_* var. Does not vary by mode.
    /// </summary>
    public string ClientBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>The dunning grace window (days) a past_due subscription's lease is extended by (ADR 0002 Decision D, AC-08). Does not vary by mode.</summary>
    public int PastDueGraceDays { get; set; } = 7;

    // --- Resolution ------------------------------------------------------------

    /// <summary>The flat legacy fields projected as a <see cref="StripeModeConfig"/> (the back-compat fallback view).</summary>
    private StripeModeConfig Flat => new()
    {
        SecretKey = SecretKey,
        WebhookSigningSecret = WebhookSigningSecret,
        PublishableKey = PublishableKey,
        PriceIds = PriceIds,
    };

    /// <summary>
    /// The effective credential set for <paramref name="mode"/>: the mode's own sub-section
    /// when it is configured, otherwise the legacy flat fields (so an old single-mode wiring
    /// keeps working as both modes). Never returns null.
    /// </summary>
    public StripeModeConfig ForMode(StripeMode mode)
    {
        var perMode = mode == StripeMode.Live ? Live : Test;
        return perMode.IsConfigured ? perMode : Flat;
    }

    /// <summary>
    /// True when billing is configured AT ALL - any mode (or the flat fallback) has a secret
    /// key. Program.cs registers the real checkout service vs. the disabled no-op on this.
    /// </summary>
    public bool IsConfigured => Live.IsConfigured || Test.IsConfigured || !string.IsNullOrWhiteSpace(SecretKey);
}
