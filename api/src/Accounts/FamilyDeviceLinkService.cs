// ----------------------------------------------------------------------------
//  FamilyDeviceLinkService - the ONE place the family-device link crypto lives
//  (accounts-identity/09, issue #229). It owns:
//    - LINK-CODE minting (AC-01): a short, human-enterable code from a DISTINCT,
//      larger alphabet/length than a room join code, generated via a CSPRNG
//      (RandomNumberGenerator), never System.Random or a Guid-treated-as-random. A
//      room code only deters a handful of concurrent rooms from colliding; a link code
//      guards entitlement access and must resist brute force even under its short
//      validity window and the redeem endpoint's rate limits.
//    - DEVICE-TOKEN minting / parsing / hashing (AC-02/AC-05): an opaque bearer token
//      that embeds ONLY the two non-secret ids AC-05 permits (the AccountId it resolves
//      to + an opaque DeviceTokenId) plus a CSPRNG secret. The server persists ONLY a
//      SHA-256 HASH of the raw token (never the raw secret) and verifies a presented
//      token by hashing it and comparing in constant time. The token is DELIBERATELY
//      not a JWT and not a Data-Protection payload, because it must be revocable
//      server-side BY ROW (a Data-Protection payload can only expire, never be revoked
//      before its TTL).
//    - The REDEEM / RESOLVE / REFRESH orchestration over the code + token stores,
//      including the rolling-TTL slide on use and the silent re-issue on refresh
//      (ADR 0003 security posture).
//    - A GLOBAL redeem/refresh throttle backstop is handled by the rate-limit policies
//      (Program.cs / FamilyDeviceRedeemRateLimit); this service is the crypto + store
//      orchestration, not the HTTP-layer limiter.
//
//  A SINGLETON: it owns no per-request state (the CSPRNG is static) and is shared by
//  the controller (mint/redeem/refresh/list/revoke/toggle) and the hub's connect-time
//  resolver (accounts-identity/06's OnConnectedAsync extension).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The outcome of redeeming a link code (accounts-identity/09, AC-02): on success the
/// RAW token to hand back to the device ONCE (only its hash is retained server-side,
/// AC-05) plus the device's non-identifying label; on failure, neither. A failure
/// carries no detail so the redeem endpoint's response reveals nothing about why.
/// </summary>
/// <param name="Success">True when the code resolved and a device token was minted + persisted.</param>
/// <param name="RawToken">The raw bearer token, returned to the device exactly once (success only).</param>
/// <param name="Label">The device's short, random, non-identifying label (success only, AC-04).</param>
public readonly record struct DeviceRedeemOutcome(bool Success, string? RawToken, string? Label)
{
    /// <summary>The shared "did not redeem" result (unknown / expired / used / burned code).</summary>
    public static readonly DeviceRedeemOutcome Miss = new(false, null, null);
}

/// <summary>
/// The connect-time resolution of a presented family-device token (accounts-identity/09,
/// AC-03/AC-07): the family <see cref="AccountId"/> the token resolves to (for the
/// entitlement evaluation) and the device's <see cref="IsAdultConfirmedDevice"/> signal
/// (for the AC-07 adult-unlock capture). The two axes are returned together but never
/// conflated - a live token always carries the AccountId (full paid capabilities),
/// independent of whether the adult-confirm flag is set.
/// </summary>
/// <param name="AccountId">The family account the live token resolves to.</param>
/// <param name="IsAdultConfirmedDevice">The device's adult-unlock signal (AC-07); false is the family-safe default.</param>
public readonly record struct ResolvedDeviceToken(Guid AccountId, bool IsAdultConfirmedDevice);

/// <summary>
/// Mints link codes and family-device tokens, and resolves / refreshes tokens
/// (accounts-identity/09). A singleton over the code + token stores. All randomness is
/// CSPRNG (AC-01); only token HASHES are ever persisted (AC-05).
/// </summary>
public sealed class FamilyDeviceLinkService
{
    /// <summary>
    /// The link-code alphabet: an unambiguous set (no I/L/O/1/0 confusion), DISTINCT
    /// from and no smaller than the room-code alphabet. 30 symbols.
    /// </summary>
    public const string LinkCodeAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <summary>
    /// The link-code length (AC-01): DELIBERATELY longer than the 4-char room join code.
    /// 30^8 is ~6.6e11 - far beyond brute force within the short validity window under
    /// the per-IP + global + per-code limits, while staying short enough to type once.
    /// </summary>
    public const int LinkCodeLength = 8;

    /// <summary>How long a minted link code stays redeemable (AC-02): minutes, like a magic link.</summary>
    public static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The rolling device-token lifetime (security posture): long-lived so a device in
    /// regular use never re-links, but not indefinite - it slides forward on each
    /// successful use, so a copied/stolen token lapses if the legitimate device keeps
    /// using its own (freshly rotated) copy.
    /// </summary>
    public static readonly TimeSpan DeviceTokenLifetime = TimeSpan.FromDays(90);

    /// <summary>The version tag prefixing every raw token, so the format can evolve without ambiguity.</summary>
    private const string TokenVersion = "v1";

    /// <summary>The number of CSPRNG secret bytes in a device token (256 bits of entropy).</summary>
    private const int TokenSecretBytes = 32;

    private readonly IFamilyLinkCodeStore _codes;
    private readonly IFamilyDeviceTokenStore _tokens;

    /// <summary>Constructs the service over the link-code ledger + the device-token store.</summary>
    public FamilyDeviceLinkService(IFamilyLinkCodeStore codes, IFamilyDeviceTokenStore tokens)
    {
        _codes = codes;
        _tokens = tokens;
    }

    /// <summary>
    /// Mints a fresh link code for <paramref name="accountId"/> (AC-01) and records it in
    /// the ledger with its short validity window. The code is CSPRNG-generated from the
    /// distinct link-code alphabet; the caller (the authenticated Account page) displays
    /// it for the parent to hand to the kid's device. Returns the code + its expiry.
    /// </summary>
    public (string Code, DateTimeOffset ExpiresUtc) MintLinkCode(Guid accountId)
    {
        var code = GenerateLinkCode();
        var expiresUtc = DateTimeOffset.UtcNow + LinkCodeLifetime;
        _codes.Mint(code, accountId, expiresUtc);
        return (code, expiresUtc);
    }

    /// <summary>
    /// Redeems a link code (AC-02): on a valid, unexpired, single-use code it mints a NEW
    /// family-device token (with <see cref="FamilyDeviceToken.IsAdultConfirmedDevice"/> =
    /// false, the SAFE default), persists only its hash + metadata, and returns the RAW
    /// token to the device ONCE (AC-05). A missing / expired / already-used / burned code
    /// returns <see cref="DeviceRedeemOutcome.Miss"/>.
    /// </summary>
    public async Task<DeviceRedeemOutcome> RedeemAsync(string code, CancellationToken ct = default)
    {
        var redemption = _codes.TryRedeem(code);
        if (!redemption.Success)
        {
            return DeviceRedeemOutcome.Miss;
        }

        var deviceTokenId = Guid.NewGuid();
        var rawToken = ComposeToken(redemption.AccountId, deviceTokenId);
        var now = DateTimeOffset.UtcNow;

        var row = new FamilyDeviceToken(
            AccountId: redemption.AccountId,
            DeviceTokenId: deviceTokenId,
            TokenHash: HashToken(rawToken),
            Label: DeviceLabelGenerator.Next(),
            CreatedUtc: now,
            LastUsedUtc: null,
            ExpiresUtc: now + DeviceTokenLifetime,
            // AC-02/AC-07: a freshly linked device defaults to the SAFE state - it unlocks
            // nothing beyond the family's paid capabilities until an adult opts it in.
            IsAdultConfirmedDevice: false,
            Revoked: false);

        await _tokens.AddAsync(row, ct);
        return new DeviceRedeemOutcome(true, rawToken, row.Label);
    }

    /// <summary>
    /// Resolves a presented family-device token to its family + adult-unlock signal for
    /// the hub's connect-time resolver (AC-03/AC-07). Parses the ids out of the token,
    /// point-reads the row, verifies the token HASH in constant time, and confirms the
    /// row is LIVE (not revoked, not expired). On success it slides the rolling TTL
    /// forward and stamps last-used (best-effort - a touch failure never fails the
    /// resolve). Returns null for any miss (unknown, tampered, revoked, expired) - which
    /// the caller treats as an anonymous connection (default-unlocked, family-safe).
    /// </summary>
    public async Task<ResolvedDeviceToken?> ResolveAsync(string? rawToken, CancellationToken ct = default)
    {
        var row = await ReadVerifiedLiveRowAsync(rawToken, ct);
        if (row is null)
        {
            return null;
        }

        // Rolling TTL slide + last-used stamp (security posture). Best-effort: a storage
        // hiccup here must never break a family's ability to play (AC-06), so a failed
        // update is swallowed - the token still resolves this session.
        var now = DateTimeOffset.UtcNow;
        var slid = row with { LastUsedUtc = now, ExpiresUtc = now + DeviceTokenLifetime };
        try
        {
            await _tokens.UpdateAsync(slid, ct);
        }
        catch
        {
            // Swallow - the resolve below still returns the family + adult signal.
        }

        return new ResolvedDeviceToken(row.AccountId, row.IsAdultConfirmedDevice);
    }

    /// <summary>
    /// Refreshes a token (security posture: rolling TTL + silent re-issue on use). Verifies
    /// the current token by hash, mints a REPLACEMENT opaque value on the SAME row,
    /// invalidates the old value immediately (its hash is overwritten), slides the TTL,
    /// stamps last-used, and returns the new raw value for the client to persist. The web
    /// client calls this once per app launch when it holds a device token, so a
    /// copied/stolen token stays valid only until the legitimate device next rotates.
    /// Returns null for any miss (unknown / tampered / revoked / expired).
    /// </summary>
    public async Task<string?> RefreshAsync(string? rawToken, CancellationToken ct = default)
    {
        var row = await ReadVerifiedLiveRowAsync(rawToken, ct);
        if (row is null)
        {
            return null;
        }

        // Mint a fresh secret on the SAME (AccountId, DeviceTokenId) - the device id is
        // stable (so the Account page's revoke/toggle handle is unchanged), only the
        // secret rotates. The old hash is overwritten by the update below, so the old raw
        // value can never resolve again.
        var newRawToken = ComposeToken(row.AccountId, row.DeviceTokenId);
        var now = DateTimeOffset.UtcNow;
        var rotated = row with
        {
            TokenHash = HashToken(newRawToken),
            LastUsedUtc = now,
            ExpiresUtc = now + DeviceTokenLifetime,
        };

        var updated = await _tokens.UpdateAsync(rotated, ct);
        // A revoked-then-deleted race (the row vanished between read and update) fails
        // the refresh cleanly rather than returning a token for a row that no longer exists.
        return updated ? newRawToken : null;
    }

    /// <summary>
    /// Parses, hash-verifies, and liveness-checks a presented token, returning the stored
    /// row only when the token is genuine and LIVE (AC-03/AC-04). Shared by resolve and
    /// refresh. NEVER throws - any malformation resolves to null (AC-06), so a corrupt or
    /// tampered token can never break the connection or the endpoint.
    /// </summary>
    private async Task<FamilyDeviceToken?> ReadVerifiedLiveRowAsync(string? rawToken, CancellationToken ct)
    {
        if (!TryParseToken(rawToken, out var accountId, out var deviceTokenId))
        {
            return null;
        }

        FamilyDeviceToken? row;
        try
        {
            row = await _tokens.GetAsync(accountId, deviceTokenId, ct);
        }
        catch
        {
            // A storage read failure is treated as "not resolvable" (AC-06) - never a throw
            // that could abort the hub connection.
            return null;
        }

        if (row is null)
        {
            return null;
        }

        // Constant-time hash comparison (the token is a bearer secret): a presented token
        // whose hash does not match this row - a forged (accountId, deviceTokenId) pointing
        // at a real row with a different secret - is rejected.
        if (!HashesEqual(row.TokenHash, HashToken(rawToken!)))
        {
            return null;
        }

        // Revoked or expired rows resolve nothing (AC-04): CreateRoom falls back to the
        // default-unlocked, family-safe baseline.
        return row.IsLiveAt(DateTimeOffset.UtcNow) ? row : null;
    }

    // --- Link code + token crypto -------------------------------------------------

    // Generate one link code from the distinct alphabet using the CSPRNG (AC-01) - the
    // SAME RandomNumberGenerator.GetInt32 the room-code generator uses, but a longer code
    // over a distinct alphabet so its keyspace dwarfs a room code's.
    private static string GenerateLinkCode()
    {
        Span<char> code = stackalloc char[LinkCodeLength];
        for (var i = 0; i < LinkCodeLength; i++)
        {
            code[i] = LinkCodeAlphabet[RandomNumberGenerator.GetInt32(LinkCodeAlphabet.Length)];
        }
        return new string(code);
    }

    // Compose a raw token: v1.{accountId}.{deviceTokenId}.{secret}. The two ids are the
    // only non-secret material AC-05 permits a token to carry; the CSPRNG secret provides
    // the entropy, and the whole string is hashed for storage so a leaked DB is inert.
    private static string ComposeToken(Guid accountId, Guid deviceTokenId)
    {
        var secret = RandomNumberGenerator.GetBytes(TokenSecretBytes);
        var secretText = Base64UrlEncode(secret);
        return $"{TokenVersion}.{accountId:N}.{deviceTokenId:N}.{secretText}";
    }

    // A generous upper bound on a well-formed token's length: v1 + two 32-char GUIDs + a
    // 43-char base64url secret + 3 separators is ~112 chars. The token is attacker-
    // controllable query input (the hub access_token), so reject anything past this cap
    // BEFORE splitting - bounding the allocation a hostile, dot-laden blob could force.
    private const int MaxRawTokenLength = 256;

    // Parse the two ids out of a presented token. Strict: a bounded length, exactly four
    // dot-separated parts (Split is capped at 5 results so a token stuffed with dots cannot
    // force an unbounded substring allocation - a 5th part fails the ==4 check), the version
    // tag, and two parseable GUIDs. Any deviation returns false (no throw, AC-06).
    private static bool TryParseToken(string? rawToken, out Guid accountId, out Guid deviceTokenId)
    {
        accountId = Guid.Empty;
        deviceTokenId = Guid.Empty;
        if (string.IsNullOrEmpty(rawToken) || rawToken.Length > MaxRawTokenLength)
        {
            return false;
        }

        var parts = rawToken.Split('.', 5);
        if (parts.Length != 4 || !string.Equals(parts[0], TokenVersion, StringComparison.Ordinal))
        {
            return false;
        }

        return Guid.TryParseExact(parts[1], "N", out accountId)
            && Guid.TryParseExact(parts[2], "N", out deviceTokenId);
    }

    // SHA-256 hex of the raw token - the ONLY token material ever persisted (AC-05).
    private static string HashToken(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    // Constant-time compare of two hex hash strings (both server-produced, so equal length
    // on a match). FixedTimeEquals over the UTF8 bytes avoids a timing oracle on the hash.
    private static bool HashesEqual(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a ?? string.Empty),
            Encoding.UTF8.GetBytes(b ?? string.Empty));

    // URL-safe base64 without padding, so the secret rides cleanly in the dot-joined token.
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
