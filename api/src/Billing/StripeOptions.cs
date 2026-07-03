// ----------------------------------------------------------------------------
//  StripeOptions - the bound configuration for the Stripe billing seam
//  (billing-entitlements/03, issue #72).
//
//  SECRETS (AC-01): SecretKey and WebhookSigningSecret are SECRETS - supplied
//  per-environment from Azure Key Vault (via an App Service app setting) or
//  user-secrets / an env var, NEVER a committed literal and NEVER a VITE_* var.
//  PublishableKey is NOT a secret (it is safe in the browser) but is kept here so
//  the client reads it from one place; the browser gets it via a VITE_* var wired
//  in stories 02/04, never from this server object.
//
//  CONFIG-PRESENCE SPLIT (the repo idiom - AI proxy / account store / grant store):
//  when SecretKey is empty (local dev, CI, a fresh clone) billing is OFF - Program.cs
//  registers the DISABLED no-op checkout service and the webhook rejects - so the
//  app runs with ZERO Stripe setup. The moment SecretKey is present, the real
//  StripeCheckoutService and webhook processing are wired.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Bound from the "Stripe" configuration section (billing-entitlements/03). Holds
/// the Stripe secret key + webhook signing secret (SECRETS, Key Vault-backed, AC-01),
/// the non-secret publishable key, and the past-due dunning grace window (AC-08).
/// </summary>
public sealed class StripeOptions
{
    /// <summary>The configuration section name these options bind from.</summary>
    public const string SectionName = "Stripe";

    /// <summary>The Stripe SECRET key (Key Vault-backed, never committed / never VITE_*). Empty => billing is OFF.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The Stripe webhook SIGNING secret (Key Vault-backed). Empty => the webhook cannot verify and rejects.</summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>The NON-secret publishable key (safe in the browser; the client gets its own copy via a VITE_* var).</summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>
    /// The dunning grace window (days) a past_due subscription's lease is EXTENDED by
    /// rather than expired (ADR 0002 Decision D, AC-08) - a failed card must not lock a
    /// family mid-ride. Defaults to 7.
    /// </summary>
    public int PastDueGraceDays { get; set; } = 7;

    /// <summary>True when a Stripe secret key is configured - the real billing path is live.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
