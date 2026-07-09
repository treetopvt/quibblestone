// ----------------------------------------------------------------------------
//  MagicLinkTokenService - the HMAC-SHA256 implementation of the reusable one-time
//  token contract (accounts-identity/02, issue #68). See IMagicLinkTokenService
//  for the full reusability contract and the `purchaser != admin` invariant.
//
//  TOKEN SHAPE: "<payload>.<signature>" where
//    payload   = "v1|<base64url(subjectUtf8)>|<expiryUnixMs>|<nonce>"
//    signature = base64url(HMACSHA256(key, payloadBytes))
//  The subject is base64url-encoded inside the payload so an opaque subject with
//  any characters (including the '|' delimiter) round-trips safely. The signature
//  covers the WHOLE payload, so neither the subject, the expiry, nor the nonce can
//  be altered without invalidating it.
//
//  SIGNING KEY (AC-06):
//    - Read from Accounts:TokenSigningKey (Key Vault-backed when deployed, NEVER a
//      committed literal). When absent (local dev / CI / a fresh clone), a random
//      32-byte key is generated at construction: tokens then work for the life of
//      the process but not across a restart, which is all a toy needs (README
//      section 4). The key material lives only in this instance's field and is
//      NEVER logged or persisted.
//
//  SINGLE USE (AC-06 + platform-devops/08 AC-07): each token carries a unique random
//  nonce (jti). On the first successful verify the nonce is CONSUMED via the injected
//  IConsumedNonceStore; a second verify of the same token finds the nonce already
//  consumed and fails. The store is DURABLE and SHARED across instances in a deployed
//  environment (so single use holds fleet-wide, not just per-process) and in-memory
//  locally - see IConsumedNonceStore. This service no longer owns the set itself;
//  consuming a nonce is a (possibly storage-bound) call, which is why TryVerifyAsync
//  is asynchronous.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// HMAC-SHA256 issuer / verifier for opaque, single-use magic-link tokens
/// (accounts-identity/02). Constant-time verification, expiry, and nonce-based
/// single-use enforcement; identity-neutral subject (see
/// <see cref="IMagicLinkTokenService"/>). Registered as a singleton so the signing
/// key is shared process-wide; the single-use nonce set lives in the injected
/// <see cref="IConsumedNonceStore"/> (durable + shared when deployed).
/// </summary>
public sealed class MagicLinkTokenService : IMagicLinkTokenService
{
    // The default token lifetime when a caller does not specify one. A magic link
    // is meant to be clicked promptly, so this is deliberately short.
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private readonly byte[] _signingKey;
    private readonly IConsumedNonceStore _consumedNonces;

    /// <summary>
    /// Constructs the service over the configured signing key (see
    /// <see cref="ConfigKeyName"/>) and the single-use nonce store. When the key is
    /// null / empty (local dev / CI), a random ephemeral key is generated so tokens
    /// work within the process lifetime. The key material is never logged or
    /// persisted (AC-06).
    ///
    /// DEPLOYED ENVIRONMENTS get a DURABLE key auto-provisioned into Key Vault
    /// (platform-devops/08 AC-04, CSPRNG-generated, wired as Accounts:TokenSigningKey):
    /// once real email delivery is on (a link now travels to an inbox and is followed
    /// minutes later), an ephemeral per-process key would make a delivered link stop
    /// verifying the moment the app recycles or scales out. The paired
    /// <paramref name="consumedNonces"/> store is likewise durable + shared when
    /// deployed (AC-07) so a single-use link cannot be replayed once per instance.
    /// See docs/runbooks/enable-magic-link-email.md.
    /// </summary>
    /// <param name="configuredSigningKey">The Accounts:TokenSigningKey value, or null / empty to use a per-process ephemeral key.</param>
    /// <param name="consumedNonces">The single-use nonce store (durable + shared when deployed, in-memory locally).</param>
    public MagicLinkTokenService(string? configuredSigningKey, IConsumedNonceStore consumedNonces)
    {
        _signingKey = string.IsNullOrWhiteSpace(configuredSigningKey)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(configuredSigningKey);
        _consumedNonces = consumedNonces;
    }

    /// <summary>The configuration key the signing secret is read from (never a committed literal, never VITE_*).</summary>
    public const string ConfigKeyName = "Accounts:TokenSigningKey";

    /// <inheritdoc />
    public string Issue(string subject, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Length == 0)
        {
            // An empty subject encodes to an empty payload segment the verifier treats
            // as malformed, so a token minted for it could never verify. Reject it at
            // issue time so the contract is explicit rather than handing back a dead
            // token. Real subjects (an email, an operator id) are never empty.
            throw new ArgumentException("A magic-link token subject must be non-empty.", nameof(subject));
        }

        var expiry = DateTimeOffset.UtcNow + (lifetime ?? DefaultLifetime);
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        var payload = BuildPayload(subject, expiry.ToUnixTimeMilliseconds(), nonce);
        var signature = Sign(payload);
        return payload + "." + signature;
    }

    /// <inheritdoc />
    public async Task<TokenVerification> TryVerifyAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return TokenVerification.Failure;
        }

        // Split off the signature (the payload itself never contains '.').
        var dot = token.LastIndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
        {
            return TokenVerification.Failure;
        }

        var payload = token[..dot];
        var providedSignature = token[(dot + 1)..];

        // Constant-time signature check: recompute the CANONICAL base64url signature
        // over the payload and compare the encoded STRINGS, not the decoded bytes.
        // Comparing decoded bytes would ACCEPT a non-canonical re-encoding of the same
        // 32-byte HMAC: a base64url-encoded 32-byte value ends in a char carrying only
        // 4 significant bits (2 are unused), so several distinct final chars decode to
        // the identical bytes - a tampered last char would verify. Comparing the
        // canonical strings admits exactly the one encoding this service produces.
        // FixedTimeEquals stays constant-time and handles a length mismatch safely.
        var expectedSignature = Sign(payload);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedSignature),
                Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return TokenVerification.Failure;
        }

        // Signature authentic - now parse the (trusted) payload fields.
        if (!TryParsePayload(payload, out var parsedSubject, out var expiryMs, out var nonce))
        {
            return TokenVerification.Failure;
        }

        // Expiry check: a token at or past its expiry is dead (and is NOT consumed,
        // so nothing changes for an expired replay).
        var expiry = DateTimeOffset.FromUnixTimeMilliseconds(expiryMs);
        if (DateTimeOffset.UtcNow >= expiry)
        {
            return TokenVerification.Failure;
        }

        // Single use: consuming the nonce IS the replay check. TryConsumeAsync
        // returns false if this nonce was already consumed by an earlier successful
        // verify - on ANY instance, because the store is shared when deployed.
        if (!await _consumedNonces.TryConsumeAsync(nonce, expiry, ct))
        {
            return TokenVerification.Failure;
        }

        return TokenVerification.Success(parsedSubject);
    }

    // ---- payload encoding -----------------------------------------------------

    private static string BuildPayload(string subject, long expiryMs, string nonce)
    {
        var subjectEncoded = EncodeBase64Url(Encoding.UTF8.GetBytes(subject));
        return $"v1|{subjectEncoded}|{expiryMs}|{nonce}";
    }

    private static bool TryParsePayload(string payload, out string subject, out long expiryMs, out string nonce)
    {
        subject = string.Empty;
        expiryMs = 0;
        nonce = string.Empty;

        var parts = payload.Split('|');
        if (parts.Length != 4 || !string.Equals(parts[0], "v1", StringComparison.Ordinal))
        {
            return false;
        }
        if (!TryDecodeBase64Url(parts[1], out var subjectBytes))
        {
            return false;
        }
        if (!long.TryParse(parts[2], out expiryMs))
        {
            return false;
        }
        if (string.IsNullOrEmpty(parts[3]))
        {
            return false;
        }

        subject = Encoding.UTF8.GetString(subjectBytes);
        nonce = parts[3];
        return true;
    }

    // ---- signing --------------------------------------------------------------

    private string Sign(string payload) => EncodeBase64Url(SignBytes(payload));

    private byte[] SignBytes(string payload) =>
        HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload));

    // ---- base64url helpers ----------------------------------------------------

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: return false; // never a valid base64 length
        }

        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
