// ----------------------------------------------------------------------------
//  PurchaserCredentialServiceTests - the shared purchaser-credential minter+resolver
//  (accounts-identity/03 + billing-entitlements/05). Confirms a minted credential
//  round-trips back to its purchaser email, and that a garbage / tampered / empty
//  credential resolves to null (treated as not-signed-in, never throws) - the
//  property the restore endpoint's 401 path relies on.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Tests.Accounts;

public class PurchaserCredentialServiceTests
{
    private static PurchaserCredentialService NewService() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_then_resolve_round_trips_the_email()
    {
        var service = NewService();

        var credential = service.Protect("buyer@example.com");
        var email = service.ResolvePurchaserEmail(credential);

        Assert.Equal("buyer@example.com", email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-credential")]
    public void Resolve_returns_null_for_missing_or_invalid_credential(string? credential)
    {
        var service = NewService();

        Assert.Null(service.ResolvePurchaserEmail(credential));
    }

    [Fact]
    public void A_credential_from_a_different_key_ring_does_not_resolve()
    {
        // A credential minted by one instance (key ring A) must not resolve under a
        // different instance (key ring B) - integrity is per-key-ring.
        var minted = NewService().Protect("buyer@example.com");

        Assert.Null(NewService().ResolvePurchaserEmail(minted));
    }
}
