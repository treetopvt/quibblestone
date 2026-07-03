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
//  SINGLE USE (AC-06): each token carries a unique random nonce (jti). On the first
//  successful verify the nonce is CONSUMED (added to an in-memory set); a second
//  verify of the same token finds the nonce already consumed and fails. The set is
//  pruned opportunistically of entries past their expiry so it cannot grow without
//  bound. The store is in-memory (thread-safe) and per-process - consistent with
//  the ephemeral-token posture above.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// HMAC-SHA256 issuer / verifier for opaque, single-use magic-link tokens
/// (accounts-identity/02). Constant-time verification, expiry, and nonce-based
/// single-use enforcement; identity-neutral subject (see
/// <see cref="IMagicLinkTokenService"/>). Registered as a singleton so the nonce
/// set and the signing key are shared process-wide.
/// </summary>
public sealed class MagicLinkTokenService : IMagicLinkTokenService
{
    // The default token lifetime when a caller does not specify one. A magic link
    // is meant to be clicked promptly, so this is deliberately short.
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    // Above this many consumed nonces, opportunistically prune expired entries so
    // the single-use set cannot grow without bound over a long-lived process.
    private const int PruneThreshold = 1024;

    private readonly byte[] _signingKey;

    // Consumed nonce (jti) -> the token's expiry, so a pruned sweep can drop
    // entries that can never be replayed anyway (they are already expired). A
    // ConcurrentDictionary gives a thread-safe TryAdd that IS the single-use check.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumedNonces =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Constructs the service over the configured signing key (see
    /// <see cref="ConfigKeyName"/>). When the key is null / empty (local dev / CI),
    /// a random ephemeral key is generated so tokens work within the process
    /// lifetime. The key material is never logged or persisted (AC-06).
    /// </summary>
    /// <param name="configuredSigningKey">The Accounts:TokenSigningKey value, or null / empty to use a per-process ephemeral key.</param>
    public MagicLinkTokenService(string? configuredSigningKey)
    {
        _signingKey = string.IsNullOrWhiteSpace(configuredSigningKey)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(configuredSigningKey);
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
    public bool TryVerify(string token, out string subject)
    {
        subject = string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // Split off the signature (the payload itself never contains '.').
        var dot = token.LastIndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
        {
            return false;
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
            return false;
        }

        // Signature authentic - now parse the (trusted) payload fields.
        if (!TryParsePayload(payload, out var parsedSubject, out var expiryMs, out var nonce))
        {
            return false;
        }

        // Expiry check: a token at or past its expiry is dead (and is NOT consumed,
        // so nothing changes for an expired replay).
        var expiry = DateTimeOffset.FromUnixTimeMilliseconds(expiryMs);
        if (DateTimeOffset.UtcNow >= expiry)
        {
            return false;
        }

        // Single use: consuming the nonce IS the replay check. TryAdd fails if this
        // nonce was already consumed by an earlier successful verify.
        if (!_consumedNonces.TryAdd(nonce, expiry))
        {
            return false;
        }

        PruneExpiredNoncesIfLarge();

        subject = parsedSubject;
        return true;
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

    // ---- single-use bookkeeping ----------------------------------------------

    private void PruneExpiredNoncesIfLarge()
    {
        if (_consumedNonces.Count < PruneThreshold)
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var (nonce, expiry) in _consumedNonces)
        {
            if (now >= expiry)
            {
                _consumedNonces.TryRemove(nonce, out _);
            }
        }
    }

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
