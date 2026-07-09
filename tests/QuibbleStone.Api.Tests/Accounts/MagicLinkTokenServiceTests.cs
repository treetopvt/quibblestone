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
    // The single-use nonce store is now an injected seam (platform-devops/08 AC-07);
    // a fresh in-memory store per service keeps each test isolated, exactly as the
    // per-process ConcurrentDictionary was before the seam was extracted.
    private static MagicLinkTokenService NewService() =>
        new("test-signing-key-not-a-real-secret", new InMemoryConsumedNonceStore());

    [Fact]
    public async Task IssueThenVerify_RoundtripsSubject()
    {
        var service = NewService();
        var token = service.Issue("buyer@example.com");

        var result = await service.TryVerifyAsync(token);
        Assert.True(result.Succeeded);
        Assert.Equal("buyer@example.com", result.Subject);
    }

    [Fact]
    public async Task TamperedToken_IsRejected()
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

        var result = await service.TryVerifyAsync(tampered);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.Subject);
    }

    // Regression for the Gate-1 base64url signature-malleability finding (CR-001):
    // the final base64url char of a 32-byte HMAC carries unused bits, so several
    // distinct final chars DECODE to the same bytes. A verifier that compared
    // decoded bytes would accept such a mutated token; comparing the canonical
    // strings must reject EVERY non-original final char. Exhaustive over the whole
    // base64url alphabet, so it is deterministic and can never flake.
    [Fact]
    public async Task SignatureMalleability_EveryNonCanonicalFinalChar_IsRejected()
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
            var result = await service.TryVerifyAsync(mutated);
            Assert.False(
                result.Succeeded,
                $"a token whose final char is '{candidate}' (original '{original}') must be rejected");
            Assert.Equal(string.Empty, result.Subject);
        }
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var service = NewService();
        // A negative lifetime mints an already-expired token.
        var token = service.Issue("buyer@example.com", TimeSpan.FromSeconds(-1));

        var result = await service.TryVerifyAsync(token);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.Subject);
    }

    [Fact]
    public async Task SingleUse_SecondVerifyFails()
    {
        var service = NewService();
        var token = service.Issue("buyer@example.com");

        Assert.True((await service.TryVerifyAsync(token)).Succeeded);
        // The nonce is consumed on the first verify; a replay must fail.
        var replay = await service.TryVerifyAsync(token);
        Assert.False(replay.Succeeded);
        Assert.Equal(string.Empty, replay.Subject);
    }

    [Fact]
    public async Task SingleUse_IsEnforcedAcrossInstances_SharingOneNonceStore()
    {
        // platform-devops/08 AC-07 (the scale-out replay gap this closes): two SEPARATE
        // service instances with the SAME signing key model two App Service instances
        // behind the load balancer. Because they SHARE one consumed-nonce store, a token
        // verified on instance A is rejected as a replay on instance B - single use holds
        // FLEET-wide, not just per-process. Before the seam this replay would have
        // succeeded once per instance.
        var sharedNonces = new InMemoryConsumedNonceStore();
        const string signingKey = "test-signing-key-not-a-real-secret";
        var instanceA = new MagicLinkTokenService(signingKey, sharedNonces);
        var instanceB = new MagicLinkTokenService(signingKey, sharedNonces);

        var token = instanceA.Issue("buyer@example.com");

        var first = await instanceA.TryVerifyAsync(token);
        Assert.True(first.Succeeded);
        Assert.Equal("buyer@example.com", first.Subject);

        // The OTHER instance must see the nonce as already consumed.
        var replayOnB = await instanceB.TryVerifyAsync(token);
        Assert.False(replayOnB.Succeeded);
        Assert.Equal(string.Empty, replayOnB.Subject);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    [InlineData("v1|bogus|123|abc.signature")]
    public async Task GarbageInput_ReturnsFalse_NeverThrows(string garbage)
    {
        var service = NewService();

        var result = await service.TryVerifyAsync(garbage);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.Subject);
    }

    [Fact]
    public async Task Subject_IsOpaque_AdminIdAndEmailBothRoundtrip()
    {
        var service = NewService();

        // An operator-id-style subject (sysadmin-console/01's reuse) and an email
        // (purchaser) both roundtrip through the SAME service - no purchaser or
        // admin meaning is baked in; the service only proves control of the subject.
        var adminToken = service.Issue("operator:admin-42");
        var emailToken = service.Issue("purchaser@example.com");

        var adminResult = await service.TryVerifyAsync(adminToken);
        Assert.True(adminResult.Succeeded);
        Assert.Equal("operator:admin-42", adminResult.Subject);

        var emailResult = await service.TryVerifyAsync(emailToken);
        Assert.True(emailResult.Succeeded);
        Assert.Equal("purchaser@example.com", emailResult.Subject);
    }
}
