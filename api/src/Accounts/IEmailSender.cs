// ----------------------------------------------------------------------------
//  IEmailSender - the SINGLE server-side seam through which QuibbleStone sends email.
//  Two messages ride it, each via its OWN method: the magic-link sign-in
//  (accounts-identity/04, issue #167, SendMagicLinkAsync) and, growing the SAME seam
//  with a second method, the game invite (session-engine/12, issue #180,
//  SendGameInviteAsync). Each send carries ONLY what that message needs - the magic
//  link carries a one-time sign-in link; the game invite carries a room's join link +
//  code - and NOTHING else. The two are distinct: a game invite has no token and no
//  MagicLinkPurpose, so it NEVER routes through SendMagicLinkAsync.
//
//  WHY ONE SEAM (AC-02): both request endpoints - the purchaser sign-in
//  (AccountsController.RequestLink) and, reusing the SAME plumbing, the operator
//  login (OperatorLoginController.RequestLink) - deliver through THIS one interface,
//  exactly as they already both reuse the ONE IMagicLinkTokenService. There is no
//  second transport: the purchaser and operator flows differ only in the copy and
//  the link they hand the sender (the MagicLinkPurpose below selects the wording),
//  never in HOW the mail is sent.
//
//  THE CONFIG-PRESENCE CONTRACT (AC-03): two implementations sit behind this one
//  interface, chosen at startup by whether an email provider is configured -
//  EXACTLY the ITelemetrySink / IAiCompletionClient / IPublishedTaleStore idiom in
//  Program.cs:
//    - AcsEmailSender  : the real Azure Communication Services Email transport,
//                        registered when the Email section is configured (a verified
//                        from-address plus either an ACS endpoint for the keyless
//                        managed-identity path or a Key Vault-backed connection
//                        string).
//    - NoOpEmailSender : the DEFAULT (local dev, CI, a fresh clone, today's deployed
//                        footprint) when no provider is configured. It does not send;
//                        it logs neutrally so the app builds + runs with ZERO email
//                        setup. The Development-only dev-token echo lives in the
//                        controllers and is UNCHANGED by this seam - a local
//                        walkthrough still completes with no provider (AC-03).
//
//  MINIMAL CONTENT / MINIMAL PII (AC-06): an implementation sends ONLY the link and
//  minimal transactional copy, to the entered address ONLY. It never receives, and
//  so never sends, a player nickname, room code, session id, or anything about a
//  player - this seam lives entirely on the purchaser / operator plane, never the
//  anonymous play plane (README section 6, the anonymity firewall).
//
//  FAIL-SAFE, NO ENUMERATION (AC-04/AC-08): delivery must never become an existence
//  oracle. The caller invokes this AFTER issuing a token for ANY well-formed email
//  (it never reads the account store / operator allowlist first), and treats a send
//  failure as a no-op behind the SAME neutral acknowledgement - so the response
//  shape and timing do not reveal whether an account / operator exists OR whether
//  the send succeeded. An implementation NEVER logs the token, the link, the email
//  body, or any secret, and NEVER retries in a way that amplifies into an
//  email-bomb vector (the per-IP SignInRateLimit / OperatorLoginRateLimit policies
//  remain the abuse boundary).
//
//  SECRETS (AC-05): the recommended ACS path is KEYLESS (the App Service managed
//  identity), so there is no provider secret at all - only the non-secret sender
//  from-address is app config. A connection-string fallback is a SECRET supplied
//  per-environment from Key Vault via an app setting - NEVER committed, NEVER a
//  VITE_* var, NEVER logged.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Which magic-link flow a delivered email belongs to (AC-02). The transport is
/// identical for both; this ONLY selects the transactional copy and subject the
/// sender uses, so the purchaser and operator flows share the one seam without a
/// second implementation. It carries NO authorization meaning (the allowlist gate
/// stays at verify time) - it is purely a copy selector.
/// </summary>
public enum MagicLinkPurpose
{
    /// <summary>A returning purchaser's sign-in / restore link (AccountsController).</summary>
    PurchaserSignIn,

    /// <summary>An operator's back-office login link (OperatorLoginController).</summary>
    OperatorLogin,
}

/// <summary>
/// The single server-side seam that delivers QuibbleStone email: the magic-link
/// sign-in (accounts-identity/04) and the game invite (session-engine/12). One real
/// implementation (ACS Email) and one no-op, resolved from DI by config presence
/// (AC-03) - there is no second transport. Sign-in and invite are DISTINCT methods: a
/// game invite is a plain notification (no token, no <see cref="MagicLinkPurpose"/>),
/// so it never routes through <see cref="SendMagicLinkAsync"/>.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Delivers the one-time magic <paramref name="link"/> to <paramref name="toEmail"/>
    /// (and ONLY that address, AC-06), with copy selected by <paramref name="purpose"/>.
    /// Fully async and honors the <paramref name="cancellationToken"/>.
    ///
    /// FAIL-SAFE (AC-08): an implementation should surface a provider failure by
    /// throwing (the caller catches it and still returns the neutral acknowledgement),
    /// OR by returning cleanly - either way it NEVER logs the token / link / body /
    /// secret and NEVER retries into an email-bomb. The caller guarantees this is only
    /// ever called with an already-issued link for a well-formed email, so a failure
    /// here can never be an account-existence oracle.
    /// </summary>
    /// <param name="toEmail">The recipient address the requester entered (the ONLY recipient, AC-06).</param>
    /// <param name="link">The already-minted one-time magic link to deliver (never logged, AC-08).</param>
    /// <param name="purpose">Selects the transactional copy (purchaser vs operator) - not an authorization signal.</param>
    /// <param name="cancellationToken">Cancellation for the send.</param>
    Task SendMagicLinkAsync(
        string toEmail,
        string link,
        MagicLinkPurpose purpose,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers a GAME INVITE email to <paramref name="toEmail"/> (session-engine/12): a
    /// fixed, templated notification carrying the room's tappable
    /// <paramref name="joinLink"/> and human-readable <paramref name="roomCode"/> - the
    /// SAME payload the Lobby's Copy/Share already hand out. This is a plain
    /// notification, NOT a sign-in: it has no token, no account, and no
    /// <see cref="MagicLinkPurpose"/>, so callers must NEVER route a game invite through
    /// <see cref="SendMagicLinkAsync"/>. Fully async and honors the
    /// <paramref name="cancellationToken"/>.
    ///
    /// FAIL-SAFE (mirrors <see cref="SendMagicLinkAsync"/>): an implementation surfaces a
    /// provider failure by throwing (the caller catches it and tells the sender it could
    /// not send) or returns cleanly; either way it NEVER logs the recipient / link / code
    /// / body and NEVER retries into an email-bomb (the per-IP EmailInviteRateLimit is the
    /// abuse boundary). The invite body is fixed template copy (no sender free text), so
    /// there is nothing here for the safety filter to check (AC-04).
    /// </summary>
    /// <param name="toEmail">The recipient address the sender entered (the ONLY recipient).</param>
    /// <param name="joinLink">The server-built `/join/&lt;code&gt;` deep link (never client-supplied).</param>
    /// <param name="roomCode">The room code shown in the readable copy (already shape-validated).</param>
    /// <param name="cancellationToken">Cancellation for the send.</param>
    Task SendGameInviteAsync(
        string toEmail,
        string joinLink,
        string roomCode,
        CancellationToken cancellationToken = default);
}
