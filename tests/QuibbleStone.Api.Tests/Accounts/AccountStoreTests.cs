// ----------------------------------------------------------------------------
//  AccountStoreTests - unit tests for the account store contract, exercised
//  through the WORKING in-memory implementation (accounts-identity/02, issue #68).
//  The Table Storage impl shares the SAME AccountIdentity normalization + key
//  scheme and the same semantics, so covering the in-memory store (no Azure
//  needed) verifies the behaviour both stores promise.
//
//  These pin the store's load-bearing guarantees:
//    1. CreateOrGet persists the stable AccountId + email + created-at, and echoes
//       the normalized email back on the account.
//    2. CreateOrGet is IDEMPOTENT (accounts-identity/02 AC-02): the same email twice
//       returns the SAME account (one row), including case-insensitively.
//    3. GetByIdentity returns null on a miss and the account on a hit.
//    4. GetByIdentity NEVER creates a row (story 03's "no create on miss").
//
//  Plus the accounts-identity/05 (#195) stable-AccountId guarantees:
//    5. AC-01: creation assigns a non-empty AccountId that is STABLE across repeat
//       CreateOrGet calls for the same email (a re-create returns the SAME id).
//    6. AC-04: GetByIdAsync(accountId) resolves the SAME account GetByIdentityAsync
//       does for that account's email (AC-06's "one AccountId per email"), and misses
//       cleanly for an unknown id without ever creating a row.
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

    // AC-01 (accounts-identity/05): creation assigns a non-empty, RANDOM AccountId.
    [Fact]
    public async Task CreateOrGet_AssignsANonEmptyAccountId()
    {
        var store = NewStore();

        var account = await store.CreateOrGetAsync("buyer@example.com");

        Assert.NotEqual(Guid.Empty, account.Id);
    }

    // AC-01: the AccountId is STABLE - a repeat CreateOrGet for the same email (any
    // case) returns the SAME id, so it never changes for the life of the account (and
    // an email change - not built here - would not need to move it).
    [Fact]
    public async Task CreateOrGet_ReturnsAStableAccountId_AcrossRepeatsAndCasing()
    {
        var store = NewStore();

        var first = await store.CreateOrGetAsync("Buyer@Example.com");
        var second = await store.CreateOrGetAsync("  buyer@example.com ");

        Assert.Equal(first.Id, second.Id);
    }

    // AC-01: two DIFFERENT emails get two DIFFERENT ids (no accidental id collision).
    [Fact]
    public async Task CreateOrGet_DistinctEmailsGetDistinctAccountIds()
    {
        var store = NewStore();

        var a = await store.CreateOrGetAsync("a@example.com");
        var b = await store.CreateOrGetAsync("b@example.com");

        Assert.NotEqual(a.Id, b.Id);
    }

    // AC-04 / AC-06: GetByIdAsync resolves the SAME account GetByIdentityAsync does for
    // that account's email - the two lookups agree on one AccountId per email.
    [Fact]
    public async Task GetById_ResolvesTheSameAccountAsGetByIdentity()
    {
        var store = NewStore();
        var created = await store.CreateOrGetAsync("Buyer@Example.com");

        var byId = await store.GetByIdAsync(created.Id);
        var byEmail = await store.GetByIdentityAsync("buyer@example.com");

        Assert.NotNull(byId);
        Assert.NotNull(byEmail);
        Assert.Equal(created.Id, byId!.Id);
        Assert.Equal(byEmail!.Id, byId.Id);
        Assert.Equal(byEmail.Email, byId.Email);
        Assert.Equal(byEmail.CreatedUtc, byId.CreatedUtc);
    }

    // AC-04: GetByIdAsync misses cleanly for an unknown id and never creates a row.
    [Fact]
    public async Task GetById_ReturnsNullOnMiss_AndNeverCreates()
    {
        var store = NewStore();

        Assert.Null(await store.GetByIdAsync(Guid.NewGuid()));
        // A later create is the FIRST creation - the prior by-id miss minted nothing.
        var created = await store.CreateOrGetAsync("buyer@example.com");
        Assert.Null(await store.GetByIdAsync(Guid.NewGuid())); // still a miss for a different id
        Assert.NotNull(await store.GetByIdAsync(created.Id));
    }
}
