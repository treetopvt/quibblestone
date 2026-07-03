// ----------------------------------------------------------------------------
//  AccountStoreTests - unit tests for the account store contract, exercised
//  through the WORKING in-memory implementation (accounts-identity/02, issue #68).
//  The Table Storage impl shares the SAME AccountIdentity normalization + key
//  scheme and the same semantics, so covering the in-memory store (no Azure
//  needed) verifies the behaviour both stores promise.
//
//  These pin the store's load-bearing guarantees:
//    1. CreateOrGet persists ONLY email + created-at (AC-01), and echoes the
//       normalized email back on the account.
//    2. CreateOrGet is IDEMPOTENT (AC-02): the same identity twice returns the
//       SAME account (one row), including case-insensitively ("A@x.com" == "a@x.com").
//    3. GetByIdentity returns null on a miss and the account on a hit.
//    4. GetByIdentity NEVER creates a row (story 03's "no create on miss").
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Tests.Accounts;

public class AccountStoreTests
{
    private static IAccountStore NewStore() => new InMemoryAccountStore();

    [Fact]
    public async Task CreateOrGet_PersistsNormalizedEmailAndCreatedAt()
    {
        var store = NewStore();
        var before = DateTimeOffset.UtcNow;

        var account = await store.CreateOrGetAsync("  Buyer@Example.com  ");

        // The one identity field is stored NORMALIZED (trim + lowercase-invariant).
        Assert.Equal("buyer@example.com", account.Email);
        // Created-at is stamped at creation time (AC-01) - a plain timestamp.
        Assert.InRange(account.CreatedUtc, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateOrGet_IsIdempotent_SameIdentityReturnsSameAccount()
    {
        var store = NewStore();

        var first = await store.CreateOrGetAsync("buyer@example.com");
        var second = await store.CreateOrGetAsync("buyer@example.com");

        // Same identity twice -> the SAME account (one row): the created-at does
        // not move on the second call (AC-02).
        Assert.Equal(first.Email, second.Email);
        Assert.Equal(first.CreatedUtc, second.CreatedUtc);
    }

    [Fact]
    public async Task CreateOrGet_IsCaseAndWhitespaceInsensitive()
    {
        var store = NewStore();

        var first = await store.CreateOrGetAsync("A@x.com");
        var second = await store.CreateOrGetAsync("  a@x.com ");

        // "A@x.com" and "a@x.com" resolve to the SAME account (identical normalization).
        Assert.Equal(first.CreatedUtc, second.CreatedUtc);
        Assert.Equal("a@x.com", second.Email);
    }

    [Fact]
    public async Task GetByIdentity_ReturnsNullOnMiss()
    {
        var store = NewStore();

        var missing = await store.GetByIdentityAsync("nobody@example.com");

        Assert.Null(missing);
    }

    [Fact]
    public async Task GetByIdentity_ReturnsAccountOnHit()
    {
        var store = NewStore();
        await store.CreateOrGetAsync("Buyer@Example.com");

        // A hit resolves case-insensitively to the created account.
        var found = await store.GetByIdentityAsync("buyer@example.com");

        Assert.NotNull(found);
        Assert.Equal("buyer@example.com", found!.Email);
    }

    [Fact]
    public async Task GetByIdentity_NeverCreatesARow()
    {
        var store = NewStore();

        // A read for an unknown identity must NOT mint an account (story 03's "no
        // create on miss"): a subsequent read still misses, and a later CreateOrGet
        // is the FIRST creation (a fresh created-at, not one a prior read would have set).
        Assert.Null(await store.GetByIdentityAsync("ghost@example.com"));
        Assert.Null(await store.GetByIdentityAsync("ghost@example.com"));

        var created = await store.CreateOrGetAsync("ghost@example.com");
        Assert.NotNull(created);
        Assert.Equal("ghost@example.com", created.Email);
    }
}
