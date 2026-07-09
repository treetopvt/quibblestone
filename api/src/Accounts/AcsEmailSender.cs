// ----------------------------------------------------------------------------
//  AcsEmailSender - the real email transport (magic-link sign-in AND game invite),
//  backed by Azure Communication Services (ACS) Email (accounts-identity/04, issue
//  #167; the game-invite method is session-engine/12, issue #180).
//
//  WHY ACS (ADR 0002 Decision A + this story's Technical Notes): it is Azure-native
//  (fits the footprint), and it authenticates KEYLESS via the App Service managed
//  identity the app already uses for Key Vault / Stripe - so the recommended path
//  stores NO provider secret at all (AC-05). A connection-string fallback is
//  supported for environments where a key is easier, and that connection string is
//  the ONLY secret, then Key Vault-backed via an app setting (never committed /
//  VITE_* / logged).
//
//  AUTH SELECTION (mirrors FoundryAiCompletionClient's managed-identity-preferred
//  posture): if Email:Endpoint is set, connect KEYLESS with DefaultAzureCredential
//  (the managed identity in Azure, a developer credential locally). Otherwise use
//  the Email:ConnectionString fallback. Program.cs only ever constructs this class
//  when EmailOptions.IsConfigured is true, so one of the two is always present.
//
//  FAIL-SAFE + NO EMAIL-BOMB (AC-08): the send uses WaitUntil.Started - it hands the
//  message to ACS and returns as soon as it is accepted, so request timing stays
//  bounded and constant (it does not block on downstream delivery). It does NOT
//  implement any resend / retry loop, so it cannot amplify into an email-bomb; the
//  per-IP rate-limit policies remain the abuse boundary. A provider error THROWS
//  (an ACS RequestFailedException, a network fault) - by design: the CALLER catches
//  it, logs neutrally, and still returns the neutral acknowledgement, so a failure
//  is never an existence oracle. This class itself never logs the recipient, the
//  token, the link, or the body (AC-08) - only the anonymous purpose on the debug
//  breadcrumb, and, on failure, the exception with no message content.
//
//  MINIMAL CONTENT / MINIMAL PII (AC-06): the email carries ONLY the one-time link
//  and minimal transactional copy, addressed to the entered address ONLY. There is
//  no player nickname, room code, or session id anywhere on this path - the sender
//  is handed a link and a recipient and knows nothing about a player.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The real ACS Email-backed magic-link transport (AC-01). Registered by Program.cs
/// only when <see cref="EmailOptions.IsConfigured"/> is true. Sends ONLY the link +
/// minimal copy to the entered address (AC-06); throws on a provider error so the
/// caller can fall back to the neutral acknowledgement (AC-08); never logs the
/// recipient / token / link / body / secret.
/// </summary>
public sealed class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly string _fromAddress;
    private readonly ILogger<AcsEmailSender> _logger;

    public AcsEmailSender(EmailOptions options, ILogger<AcsEmailSender> logger)
    {
        // Program.cs guarantees IsConfigured, so FromAddress is present and at least
        // one transport is set. FromAddress is trimmed to a non-null local for use.
        _fromAddress = (options.FromAddress ?? string.Empty).Trim();
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            // Preferred: keyless via the App Service managed identity (no secret).
            // DefaultAzureCredential resolves the managed identity in Azure and a
            // developer credential locally.
            _client = new EmailClient(new Uri(options.Endpoint.Trim()), new DefaultAzureCredential());
            // A construction breadcrumb (no secret) so a misconfigured deploy is fast
            // to diagnose - which auth path was chosen, never the endpoint / key.
            _logger.LogInformation("ACS email sender initialized (keyless via managed identity).");
        }
        else
        {
            // Fallback: the Key Vault-backed connection string (the only secret path).
            _client = new EmailClient(options.ConnectionString!.Trim());
            _logger.LogInformation("ACS email sender initialized (connection-string fallback).");
        }
    }

    /// <inheritdoc />
    public async Task SendMagicLinkAsync(
        string toEmail,
        string link,
        MagicLinkPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        // Build the minimal, purpose-appropriate copy (AC-06). Nothing here is player
        // data - only the link and a one-line explanation of what it is for.
        var (subject, greeting) = purpose switch
        {
            MagicLinkPurpose.OperatorLogin =>
                ("Your QuibbleStone operator sign-in link",
                 "Here is your one-time link to sign in to the QuibbleStone operator console."),
            MagicLinkPurpose.FamilySignUp =>
                // accounts-identity/07: a FREE family account is created without a purchase,
                // so "restore your purchase" would read wrong here. Copy reflects create-or-open,
                // not restore - the ONLY difference from the purchaser path (same transport, same token).
                ("Your QuibbleStone family account link",
                 "Here is your one-time link to create or open your QuibbleStone family account."),
            _ =>
                ("Your QuibbleStone sign-in link",
                 "Here is your one-time link to sign in to QuibbleStone and restore your purchase."),
        };

        // Plain-text and HTML bodies. Both carry ONLY the greeting, the link, and a
        // short "did not request this? ignore it" note - no player / room / session
        // data (AC-06). The link text is the link itself so a plain-text client is
        // fully usable.
        var plainText =
            $"{greeting}\n\n{link}\n\n" +
            "This link can be used once and expires shortly. " +
            "If you did not request it, you can safely ignore this email.";

        // The link is server-built (MagicLinkTokenService output, URL-escaped) so there
        // is no user-controlled HTML here today; HTML-encode it anyway so it stays safe
        // and correct if the link ever gains a user-influenced segment (defensive).
        var encodedLink = System.Net.WebUtility.HtmlEncode(link);
        var html =
            $"<p>{greeting}</p>" +
            $"<p><a href=\"{encodedLink}\">Sign in to QuibbleStone</a></p>" +
            $"<p>Or paste this link into your browser:<br>{encodedLink}</p>" +
            "<p>This link can be used once and expires shortly. " +
            "If you did not request it, you can safely ignore this email.</p>";

        var content = new EmailContent(subject)
        {
            PlainText = plainText,
            Html = html,
        };

        // Addressed to the entered address ONLY (AC-06).
        var message = new EmailMessage(_fromAddress, toEmail, content);

        // Log an anonymous breadcrumb BEFORE the send - purpose only, never the
        // recipient / token / link / body (AC-08).
        _logger.LogDebug("Sending magic-link email via ACS (purpose={Purpose}).", purpose);

        // WaitUntil.Started: accept-and-return, no blocking on downstream delivery and
        // no resend loop (AC-08). A provider fault throws out of here; the caller
        // catches it and returns the neutral acknowledgement.
        await _client.SendAsync(WaitUntil.Started, message, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SendGameInviteAsync(
        string toEmail,
        string joinLink,
        string roomCode,
        CancellationToken cancellationToken = default)
    {
        // session-engine/12: a FIXED game-invite template - no token, no MagicLinkPurpose.
        // The copy mirrors the Lobby's Copy/Share text (useRoomInvite.share) so the email
        // reads like the SAME product, not a different copy deck (AC-01). The only
        // substitutions are the room code and the server-built join link; there is no
        // sender free text anywhere, so nothing here needs the safety filter (AC-04).
        const string subject = "You're invited to a QuibbleStone game!";

        var plainText =
            $"Join my QuibbleStone game! Room code: {roomCode}\n\n{joinLink}\n\n" +
            "Tap the link to hop straight into the room, or type the room code on the join " +
            "screen. See you round the stone!";

        // The join link is server-built from a shape-validated code (never client HTML),
        // but HTML-encode both substitutions anyway so the email stays safe and correct if
        // either ever gains a user-influenced segment (defensive, mirrors the magic-link body).
        var encodedLink = System.Net.WebUtility.HtmlEncode(joinLink);
        var encodedCode = System.Net.WebUtility.HtmlEncode(roomCode);
        var html =
            $"<p>Join my QuibbleStone game! Room code: <strong>{encodedCode}</strong></p>" +
            $"<p><a href=\"{encodedLink}\">Tap here to join the game</a></p>" +
            $"<p>Or paste this link into your browser:<br>{encodedLink}</p>" +
            "<p>See you round the stone!</p>";

        var content = new EmailContent(subject)
        {
            PlainText = plainText,
            Html = html,
        };

        // Addressed to the entered address ONLY.
        var message = new EmailMessage(_fromAddress, toEmail, content);

        // Anonymous breadcrumb only - never the recipient / link / code / body.
        _logger.LogDebug("Sending game-invite email via ACS.");

        // WaitUntil.Started: accept-and-return, no resend loop. A provider fault throws;
        // the caller (EmailInviteController) catches it and returns a friendly result.
        await _client.SendAsync(WaitUntil.Started, message, cancellationToken);
    }
}
