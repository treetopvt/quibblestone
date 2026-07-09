// ----------------------------------------------------------------------------
//  VaultIdTests - covers the vault-id bearer-credential primitive for
//  keepsake-vault/01 (issue #196, ADR 0003 "Handles are secrets").
//
//  The security-relevant bits to lock in (AC-01): the server-side length/format
//  FLOOR accepts a real crypto.randomUUID() / server-minted token and rejects a
//  weak / forged one, and the server minter produces a floor-passing, CSPRNG-backed
//  id (never a Math.random-weak one).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public class VaultIdTests
{
    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111")] // a UUID (36 chars, hex + hyphens)
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")] // a long hex token
    public void IsWellFormed_accepts_a_uuid_or_a_long_random_token(string candidate)
    {
        Assert.True(VaultId.IsWellFormed(candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]                              // below the 36-char floor
    [InlineData("1111111111111111111111111111111111 ")]   // 36 chars but a space is not token-shaped
    [InlineData("11111111-1111-4111-8111-11111111111!")]   // a disallowed glyph
    public void IsWellFormed_rejects_a_weak_or_malformed_id(string? candidate)
    {
        Assert.False(VaultId.IsWellFormed(candidate));
    }

    [Fact]
    public void IsWellFormed_rejects_an_overlong_id()
    {
        Assert.False(VaultId.IsWellFormed(new string('a', VaultId.MaxLength + 1)));
    }

    [Fact]
    public void Mint_produces_a_floor_passing_unguessable_id()
    {
        var a = VaultId.Mint();
        var b = VaultId.Mint();

        Assert.True(VaultId.IsWellFormed(a));
        Assert.True(a.Length >= VaultId.MinLength);
        Assert.NotEqual(a, b); // cryptographically random - two draws differ
    }
}
