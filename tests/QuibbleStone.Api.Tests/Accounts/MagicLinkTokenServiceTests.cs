// ----------------------------------------------------------------------------
//  MagicLinkTokenServiceTests - unit tests for the reusable one-time token service
//  (accounts-identity/02, issue #68).
//
//  These pin the security guarantees the magic-link flow (and sysadmin-console/01,
//  which reuses this service) depends on:
//    1. issue -> verify roundtrips the SAME subject.
//    2. a TAMPERED token is rejected.
//    3. an EXPIRED token (negative lifetime) is rejected.
//    4. SINGLE USE: a second verify of the same token fails (replay defence).
//    5. garbage / empty input returns false (never throws).
//    6. the subject is OPAQUE: an admin-style operator id AND an email both
//       roundtrip, proving no purchaser semantics are baked in (reusability).
//
//  A fixed signing key is passed so tests are deterministic; the service also
//  works with a null key (per-process ephemeral), but a fixed key keeps assertions
//  stable. The signing key here is a TEST literal only - the real key is config /
//  Key Vault supplied (AC-06).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Tests.Accounts;

public class MagicLinkTokenServiceTests
{
    private static MagicLinkTokenService NewService() =>
        new("test-signing-key-not-a-real-secret");

    [Fact]
    public void IssueThenVerify_RoundtripsSubject()
    {
        var service = NewService();
        var token = service.Issue("buyer@example.com");

        Assert.True(service.TryVerify(token, out var subject));
        Assert.Equal("buyer@example.com", subject);
    }

    [Fact]
    public void TamperedToken_IsRejected()
    {
        var service = NewService();
        var token = service.Issue("buyer@example.com");

        // Mutate a character INSIDE the payload (the first char of the encoded
        // subject, just after "v1|") - a deterministic change that must invalidate
        // the HMAC. Deliberately NOT the final char, whose malleability is covered
        // exhaustively below.
        var idx = token.IndexOf('|') + 1;
        var replacement = token[idx] == 'A' ? 'B' : 'A';
        var tampered = token[..idx] + replacement + token[(idx + 1)..];

        Assert.False(service.TryVerify(tampered, out var subject));
        Assert.Equal(string.Empty, subject);
    }

    // Regression for the Gate-1 base64url signature-malleability finding (CR-001):
    // the final base64url char of a 32-byte HMAC carries unused bits, so several
    // distinct final chars DECODE to the same bytes. A verifier that compared
    // decoded bytes would accept such a mutated token; comparing the canonical
    // strings must reject EVERY non-original final char. Exhaustive over the whole
    // base64url alphabet, so it is deterministic and can never flake.
    [Fact]
    public void SignatureMalleability_EveryNonCanonicalFinalChar_IsRejected()
    {
        const string base64UrlAlphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var service = NewService();
        var token = service.Issue("buyer@example.com");
        var original = token[^1];

        foreach (var candidate in base64UrlAlphabet)
        {
            if (candidate == original)
            {
                continue;
            }

            var mutated = token[..^1] + candidate;
            Assert.False(
                service.TryVerify(mutated, out var subject),
                $"a token whose final char is '{candidate}' (original '{original}') must be rejected");
            Assert.Equal(string.Empty, subject);
        }
    }

    [Fact]
    public void ExpiredToken_IsRejected()
    {
        var service = NewService();
        // A negative lifetime mints an already-expired token.
        var token = service.Issue("buyer@example.com", TimeSpan.FromSeconds(-1));

        Assert.False(service.TryVerify(token, out var subject));
        Assert.Equal(string.Empty, subject);
    }

    [Fact]
    public void SingleUse_SecondVerifyFails()
    {
        var service = NewService();
        var token = service.Issue("buyer@example.com");

        Assert.True(service.TryVerify(token, out _));
        // The nonce is consumed on the first verify; a replay must fail.
        Assert.False(service.TryVerify(token, out var subject));
        Assert.Equal(string.Empty, subject);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    [InlineData("v1|bogus|123|abc.signature")]
    public void GarbageInput_ReturnsFalse_NeverThrows(string garbage)
    {
        var service = NewService();

        Assert.False(service.TryVerify(garbage, out var subject));
        Assert.Equal(string.Empty, subject);
    }

    [Fact]
    public void Subject_IsOpaque_AdminIdAndEmailBothRoundtrip()
    {
        var service = NewService();

        // An operator-id-style subject (sysadmin-console/01's reuse) and an email
        // (purchaser) both roundtrip through the SAME service - no purchaser or
        // admin meaning is baked in; the service only proves control of the subject.
        var adminToken = service.Issue("operator:admin-42");
        var emailToken = service.Issue("purchaser@example.com");

        Assert.True(service.TryVerify(adminToken, out var adminSubject));
        Assert.Equal("operator:admin-42", adminSubject);

        Assert.True(service.TryVerify(emailToken, out var emailSubject));
        Assert.Equal("purchaser@example.com", emailSubject);
    }
}
