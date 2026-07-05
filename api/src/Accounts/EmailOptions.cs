// ----------------------------------------------------------------------------
//  EmailOptions - the bound "Email" configuration section for magic-link delivery
//  (accounts-identity/04, issue #167). Mirrors AiOptions / StripeOptions: one small
//  options object, registered as a singleton, read once at startup so the
//  config-presence gate in Program.cs picks the real sender vs the no-op.
//
//  WHAT IS AND IS NOT A SECRET (AC-05):
//    - FromAddress and Endpoint and LinkBaseUrl are NON-secret app config. The
//      recommended ACS path is KEYLESS (the App Service managed identity), so with
//      an Endpoint set there is NO provider secret at all.
//    - ConnectionString is the ONLY secret here, and ONLY on the connection-string
//      fallback path. It is supplied per-environment from Key Vault via an app
//      setting (the Stripe / Accounts pattern) - NEVER committed to appsettings.json,
//      NEVER a VITE_* var, NEVER logged.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The bound "Email" configuration section (accounts-identity/04). Selects and
/// configures the magic-link email transport. Absent / incomplete =&gt; the app runs
/// on the NoOpEmailSender with zero email setup (AC-03).
/// </summary>
public sealed class EmailOptions
{
    /// <summary>The configuration section name ("Email").</summary>
    public const string SectionName = "Email";

    /// <summary>
    /// The Azure Communication Services Email resource endpoint (e.g.
    /// "https://my-acs.communication.azure.com"). NON-secret. When set, the real
    /// sender authenticates KEYLESS via the App Service managed identity (the
    /// recommended path, no provider secret to store, AC-05).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// The ACS connection-string FALLBACK (contains an access key). A SECRET -
    /// supplied per-environment from Key Vault via an app setting, NEVER committed
    /// and NEVER a VITE_* var (AC-05). Only used when <see cref="Endpoint"/> is not
    /// set; the keyless endpoint path is preferred.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The verified sender address the link is sent FROM (e.g.
    /// "no-reply@quibblestone.com"). NON-secret, but ALWAYS required for a real send
    /// - it must be a domain verified in ACS with SPF / DKIM (AC-09, see the runbook).
    /// </summary>
    public string? FromAddress { get; set; }

    /// <summary>
    /// The public web origin the magic link points at (e.g. "https://quibblestone.com").
    /// NON-secret. The controllers build the clickable link as {LinkBaseUrl}{path}?token=...
    /// When absent, the controller falls back to the incoming request's own origin
    /// (fine for a local dev walkthrough, which uses the dev-token echo anyway).
    /// </summary>
    public string? LinkBaseUrl { get; set; }

    /// <summary>
    /// True only when enough is configured to actually send: a verified from-address
    /// PLUS a transport (an endpoint for the keyless path OR a connection string).
    /// Program.cs registers the real AcsEmailSender only when this is true; otherwise
    /// the NoOpEmailSender keeps the app running with zero email setup (AC-03).
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(FromAddress)
        && (!string.IsNullOrWhiteSpace(Endpoint) || !string.IsNullOrWhiteSpace(ConnectionString));
}
