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

        // Flip the final character (part of the signature / payload) - any single
        // mutation must invalidate the HMAC.
        var lastChar = token[^1];
        var replacement = lastChar == 'A' ? 'B' : 'A';
        var tampered = token[..^1] + replacement;

        Assert.False(service.TryVerify(tampered, out var subject));
        Assert.Equal(string.Empty, subject);
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
