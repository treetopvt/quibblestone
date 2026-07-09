// ----------------------------------------------------------------------------
//  PublishedTaleTests - pure-logic unit tests for keepsake-gallery/04 (issue #66):
//  the unguessable slug generator (AC-03) and the TTL expiry-on-read rule (AC-05).
//
//  These are the parts that MUST hold regardless of storage: a slug is drawn only
//  from the unambiguous alphabet, is long enough to resist enumeration, and varies
//  draw to draw; and an expired tale reads as GONE. The full Table Storage round-
//  trip is not exercised here (it needs a live/emulated Storage account, exactly
//  like the telemetry sink) - the controller re-vet/404 behavior is covered in
//  PublishedTalesControllerTests against an in-memory fake store.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.PublishedTales;

namespace QuibbleStone.Api.Tests;

public class PublishedTaleSlugTests
{
    [Fact]
    public void Slug_is_the_declared_length()
    {
        var slug = SlugGenerator.Generate();
        Assert.Equal(SlugGenerator.SlugLength, slug.Length);
    }

    [Fact]
    public void Slug_is_at_least_ten_chars_so_it_resists_enumeration()
    {
        // AC-03: much longer than the 4-char join code. Pin the floor so a future
        // shortening is a deliberate, reviewed decision.
        Assert.True(SlugGenerator.SlugLength >= 10);
    }

    [Fact]
    public void Slug_uses_only_the_unambiguous_alphabet()
    {
        // Draw a good number so any stray glyph would surface. Every character must
        // come from the no-ambiguous-glyph alphabet (no O/0/I/1/l).
        for (var i = 0; i < 500; i++)
        {
            var slug = SlugGenerator.Generate();
            foreach (var ch in slug)
            {
                Assert.Contains(ch, SlugGenerator.Alphabet);
            }
        }
    }

    [Fact]
    public void Alphabet_excludes_the_look_alike_glyphs()
    {
        foreach (var ambiguous in new[] { 'O', '0', 'I', '1', 'L', 'l' })
        {
            Assert.DoesNotContain(ambiguous, SlugGenerator.Alphabet);
        }
    }

    [Fact]
    public void Slugs_are_not_sequential_and_vary_between_draws()
    {
        // AC-03: cryptographically random, never sequential. 1000 draws must be
        // (essentially certainly) all distinct given the ~7.9e17 keyspace.
        var slugs = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(slugs.Add(SlugGenerator.Generate()), "Slug generator produced a duplicate.");
        }
    }
}

public class PublishedTaleExpiryTests
{
    private static PublishedTale TaleExpiringAt(DateTimeOffset expiresUtc) =>
        new(
            Slug: "SLUGSLUGSLUG",
            Title: "A tale",
            Parts: [new TalePart(false, "Once upon a "), new TalePart(true, "banana")],
            BylineNames: "Sam & Mia",
            CreatedUtc: expiresUtc - TimeSpan.FromDays(30),
            ExpiresUtc: expiresUtc);

    [Fact]
    public void A_future_expiry_is_not_expired()
    {
        var now = DateTimeOffset.UtcNow;
        var tale = TaleExpiringAt(now + TimeSpan.FromDays(1));
        Assert.False(tale.IsExpired(now));
    }

    [Fact]
    public void A_past_expiry_reads_as_gone()
    {
        // AC-05: an expired tale reads as GONE (lazy expiry-on-read).
        var now = DateTimeOffset.UtcNow;
        var tale = TaleExpiringAt(now - TimeSpan.FromSeconds(1));
        Assert.True(tale.IsExpired(now));
    }

    [Fact]
    public void Expiry_is_inclusive_at_the_exact_instant()
    {
        // ExpiresUtc <= now is gone: the exact expiry instant counts as expired.
        var now = DateTimeOffset.UtcNow;
        var tale = TaleExpiringAt(now);
        Assert.True(tale.IsExpired(now));
    }
}

// keepsake-vault/04: the pure moderation-takedown soft-delete helpers.
public class PublishedTaleTakedownTests
{
    private static PublishedTale Tale(DateTimeOffset? deletedUtc) =>
        new(
            Slug: "SLUGSLUGSLUG",
            Title: "A tale",
            Parts: [new TalePart(false, "Once upon a "), new TalePart(true, "banana")],
            BylineNames: "Sam & Mia",
            CreatedUtc: DateTimeOffset.UtcNow - TimeSpan.FromDays(1),
            ExpiresUtc: DateTimeOffset.UtcNow + TimeSpan.FromDays(30),
            DeletedUtc: deletedUtc);

    [Fact]
    public void A_serving_tale_is_not_taken_down()
    {
        var tale = Tale(deletedUtc: null);
        Assert.False(tale.IsTakenDown);
        Assert.False(tale.IsRestoreWindowElapsed(DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public void A_taken_down_tale_is_recoverable_until_the_window_elapses()
    {
        // AC-02/AC-03: the takedown restore window is DeletedUtc + window, inclusive.
        var deleted = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tale = Tale(deletedUtc: deleted);

        Assert.True(tale.IsTakenDown);
        Assert.False(tale.IsRestoreWindowElapsed(deleted.AddDays(PublishedTale.TakedownRestoreWindowDays - 1)));
        Assert.True(tale.IsRestoreWindowElapsed(deleted.AddDays(PublishedTale.TakedownRestoreWindowDays)));
        Assert.True(tale.IsRestoreWindowElapsed(deleted.AddDays(PublishedTale.TakedownRestoreWindowDays + 5)));
    }

    [Fact]
    public void The_takedown_window_matches_the_vault_restore_window()
    {
        // AC-04: the SAME restore-window model as the vault's own soft-delete.
        Assert.Equal(QuibbleStone.Api.Vault.VaultTale.RestoreWindowDays, PublishedTale.TakedownRestoreWindowDays);
    }
}
