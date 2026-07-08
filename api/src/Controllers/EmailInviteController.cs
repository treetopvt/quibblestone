// ----------------------------------------------------------------------------
//  EmailInviteController - the stateless REST surface that emails a game invite
//  (session-engine/12, issue #180). It adds a THIRD invite channel to the Lobby
//  alongside Copy and Share (useRoomInvite): type a friend's email, tap Send, and
//  QuibbleStone delivers the SAME join-link + room-code payload Copy/Share already
//  hand out - so the sender never has to leave the app to paste a link themselves.
//
//  DELIBERATELY OFF THE HUB / ROOM STATE (AC-02): every existing email send in this
//  codebase is REST (AccountsController, OperatorLoginController), never the hub, and
//  GameHub has no email dependency. This controller mutates no room state, needs no
//  SignalR round-trip, and never touches Room.cs / RoomRegistry.cs / GameHub.cs -
//  exactly as Copy/Share act on the room code today with no server call at all. It
//  only SHAPE-validates the code (see below); it never looks a room up.
//
//  REUSES THE ONE EMAIL SEAM (AC-03): it delivers through the SAME IEmailSender the
//  magic-link flow uses, via that interface's OWN game-invite method
//  (SendGameInviteAsync) with its own fixed template - it NEVER calls
//  SendMagicLinkAsync and carries no token / MagicLinkPurpose, because a game invite is
//  a plain notification, not a sign-in.
//
//  THE SERVER BUILDS THE LINK (a security note, not a style preference): a "from
//  QuibbleStone" email whose link came from client input would be an open-relay /
//  phishing vector. So the client sends ONLY { roomCode, toEmail } - never a URL - and
//  the server shape-validates the code and builds {base}/join/{code} itself (base =
//  EmailOptions.LinkBaseUrl, the same public origin the magic link uses, falling back
//  to the request origin). The code is validated against the SAME alphabet/length
//  RoomRegistry mints from, mirrored here deliberately (AC-02 forbids touching
//  RoomRegistry.cs) the way Join.tsx mirrors them client-side.
//
//  CHILD SAFETY / PRIVACY (AC-04, README section 6): the email body is FIXED templated
//  copy (room code + join link) with NO free-text field for the sender to fill in - so
//  there is nothing here for the profanity filter to check. The only datum collected is
//  the recipient address the sender chose to enter; it is used once for this send and
//  never stored. No player is asked for it; no minor's data is involved.
//
//  DEGRADES CLEANLY (AC-06): with no email provider configured (EmailOptions.IsConfigured
//  == false, today's default) the GET availability probe returns false and the Lobby
//  hides/disables the control BEFORE the player types anything; a POST that arrives anyway
//  returns a friendly not-available result, never a silent no-op or a raw error.
//
//  NOT GATED (AC-07): any player in the room may use this, not only the host - the room
//  code is already visible to every player on the Lobby screen (mirrors session-engine/11).
//  There is no account / entitlement check anywhere on this path.
//
//  ABUSE (AC-05): POST opts into the per-IP EmailInviteRateLimit (a sibling of
//  SignInRateLimit / PublishTalesRateLimit) so it cannot become a scripted email-bombing
//  relay.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Invite;

namespace QuibbleStone.Api.Controllers;

/// <summary>Response for GET /api/invite/availability: whether email invites can be sent right now (AC-06).</summary>
/// <param name="Available">True only when an email provider is configured (EmailOptions.IsConfigured).</param>
public sealed record EmailInviteAvailabilityResult(bool Available);

/// <summary>Request body for POST /api/invite/email (AC-04): ONLY the room code + recipient - no URL, no free text.</summary>
/// <param name="RoomCode">The room's join code (shape-validated server-side; never looked up).</param>
/// <param name="ToEmail">The single recipient address the sender entered (used once, never stored).</param>
public sealed record EmailInviteRequestBody(string? RoomCode, string? ToEmail);

/// <summary>Response for POST /api/invite/email: whether the invite was sent, plus a friendly message when not.</summary>
/// <param name="Sent">True when the invite was handed to the email provider; false on a not-available / bad-input / send-failure outcome.</param>
/// <param name="Message">A friendly message for a not-sent outcome (null on success).</param>
public sealed record EmailInviteResult(bool Sent, string? Message);

[ApiController]
[Route("api/invite")]
public sealed class EmailInviteController : ControllerBase
{
    // The room-code shape, mirrored from RoomRegistry's private consts DELIBERATELY:
    // AC-02 forbids this stateless endpoint from touching RoomRegistry.cs, so - exactly
    // as Join.tsx mirrors the same alphabet/length client-side - we shape-gate here (no
    // ambiguous glyphs; a fixed 4-char length) and never look a room up. A dead room's
    // link already fails gracefully at JOIN time, not at invite time.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 4;

    // Max accepted recipient length (mirrors AccountsController.MaxEmailLength): the RFC
    // 5321 ceiling. Anything longer is not a real address - reject it before a send.
    private const int MaxEmailLength = 254;

    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<EmailInviteController> _logger;

    public EmailInviteController(
        IEmailSender email,
        EmailOptions emailOptions,
        ILogger<EmailInviteController> logger)
    {
        _email = email;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/invite/availability -> { available } (AC-06). The Lobby reads this ONCE
    /// before rendering the email-invite control and fails toward hidden/disabled, so a
    /// player learns email invites are off BEFORE typing, never after a dead submit.
    /// Mirrors GET /api/billing/products' { enabled } posture. Not rate-limited (a cheap
    /// read); no room / player / PII touched.
    /// </summary>
    [HttpGet("availability")]
    public IActionResult Availability() =>
        Ok(new EmailInviteAvailabilityResult(_emailOptions.IsConfigured));

    /// <summary>
    /// POST /api/invite/email { roomCode, toEmail } -> { sent, message? }. Delivers the
    /// SAME join-link + room-code payload Copy/Share produce, through the ONE IEmailSender
    /// seam's game-invite method (AC-01/AC-03). Stateless: no hub, no room lookup (AC-02).
    /// Rate-limited per IP (AC-05). Any player may call it (AC-07).
    /// </summary>
    [HttpPost("email")]
    [EnableRateLimiting(EmailInviteRateLimit.PolicyName)]
    public async Task<IActionResult> SendInvite([FromBody] EmailInviteRequestBody? request)
    {
        // AC-06: degrade cleanly when no provider is configured. The control is hidden via
        // the availability probe, but a POST that arrives anyway gets a friendly result,
        // never a silent success (the NoOp sender would swallow it) or a raw error.
        if (!_emailOptions.IsConfigured)
        {
            return Ok(new EmailInviteResult(
                Sent: false,
                Message: "Email invites aren't available right now - use Copy or Share instead."));
        }

        // Shape-validate the code (AC-02): normalized to the uppercase alphabet the codes
        // are minted from. Never a RoomRegistry lookup - a bad SHAPE is rejected here; a
        // well-shaped-but-dead code fails later at JOIN time, exactly like Copy/Share.
        var code = (request?.RoomCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!IsWellFormedRoomCode(code))
        {
            return BadRequest(new EmailInviteResult(
                Sent: false,
                Message: "That room code doesn't look right - double-check it and try again."));
        }

        // A light shape gate on the recipient (ACS is the real validator): reject obvious
        // junk before a send attempt so a typo fails fast and friendly, not as a 500.
        var toEmail = (request?.ToEmail ?? string.Empty).Trim();
        if (!IsPlausibleEmail(toEmail))
        {
            return BadRequest(new EmailInviteResult(
                Sent: false,
                Message: "That email address doesn't look right - please check it and try again."));
        }

        // Build the join link SERVER-SIDE (never a client-supplied URL - the open-relay
        // guard): {base}/join/{code}, mirroring useRoomInvite's /join/:code deep link and
        // AccountsController.BuildMagicLink's base resolution.
        var joinLink = BuildJoinLink(code);

        try
        {
            // The ONE seam's game-invite method (AC-03) - never SendMagicLinkAsync. Flow
            // the request-aborted token so a disconnect / shutdown cancels the send.
            await _email.SendGameInviteAsync(toEmail, joinLink, code, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Unlike the sign-in flow there is no account-enumeration concern here (the
            // sender chose the recipient), so we honestly report a could-not-send - but we
            // log only the exception, never the recipient / link / code (privacy).
            _logger.LogWarning(ex, "Game-invite email delivery failed; returning a friendly could-not-send result.");
            return Ok(new EmailInviteResult(
                Sent: false,
                Message: "We couldn't send that invite just now - please try Copy or Share instead."));
        }

        return Ok(new EmailInviteResult(Sent: true, Message: null));
    }

    /// <summary>Shape-only room-code check against the mirrored alphabet + length (AC-02) - never a RoomRegistry lookup.</summary>
    private static bool IsWellFormedRoomCode(string code) =>
        code.Length == CodeLength && code.All(ch => CodeAlphabet.Contains(ch));

    /// <summary>
    /// A light recipient shape gate (not full RFC validation): non-empty, within the RFC
    /// ceiling, exactly one '@' with something either side, and no spaces. ACS does the
    /// real validation; this just rejects obvious junk before a send is attempted.
    /// </summary>
    private static bool IsPlausibleEmail(string email)
    {
        if (email.Length == 0 || email.Length > MaxEmailLength) return false;
        var at = email.IndexOf('@');
        if (at <= 0 || at >= email.Length - 1) return false;
        if (email.IndexOf('@', at + 1) >= 0) return false; // more than one '@'
        return !email.Contains(' ');
    }

    /// <summary>
    /// Builds {LinkBaseUrl-or-request-origin}/join/{code} (AC-01) - the SAME /join/:code
    /// deep-link shape useRoomInvite builds client-side. The base reuses the magic link's
    /// EmailOptions.LinkBaseUrl (falling back to the request origin), never a client value.
    /// </summary>
    private string BuildJoinLink(string code)
    {
        var linkBase = (_emailOptions.LinkBaseUrl ?? string.Empty).Trim();
        if (linkBase.Length == 0)
        {
            linkBase = $"{Request.Scheme}://{Request.Host}";
        }

        return $"{linkBase.TrimEnd('/')}/join/{code}";
    }
}
