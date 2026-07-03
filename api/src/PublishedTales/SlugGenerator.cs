// ----------------------------------------------------------------------------
//  SlugGenerator - mints the unguessable public slug for a shareable tale link
//  (keepsake-gallery/04, AC-03).
//
//  A published tale is private-by-obscurity: there is no directory, so the slug
//  in `https://<app>/t/<slug>` is the ONLY thing standing between the public and
//  a family's tale. It therefore MUST resist enumeration (AC-03):
//
//    - CRYPTOGRAPHICALLY RANDOM, never sequential and never time-derived. Uses
//      System.Security.Cryptography.RandomNumberGenerator.GetInt32 for an
//      unbiased pick across the alphabet (the same primitive the room-code
//      generator uses, RoomRegistry.GenerateCode).
//    - LONG. The 4-char join code (session-engine) only has to be unique among a
//      handful of concurrently-active rooms and is read aloud in a car; a public
//      link has no such lifetime bound, so it is much longer. At length 12 over
//      the 31-glyph unambiguous alphabet the keyspace is 31^12 (~7.9e17) - guessing
//      a live slug is infeasible.
//    - The SAME no-ambiguous-glyph alphabet as the join code (O/0/I/1/l removed)
//      so a slug stays human-shareable (readable aloud, typo-resistant) without
//      re-introducing look-alike confusion.
//
//  This is pure, static, and dependency-free so it is trivially unit-tested
//  (alphabet membership, length, non-repetition across draws).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// Mints cryptographically-random, unguessable public tale slugs (AC-03). Pure
/// and static - no state, no DI.
/// </summary>
public static class SlugGenerator
{
    /// <summary>
    /// The unambiguous slug alphabet: A-Z and 2-9 with the look-alike glyphs
    /// (O, 0, I, 1, lowercase l) removed, matching session-engine's join-code
    /// alphabet so a slug stays easy to read aloud and type.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// The slug length. 12 characters over the 31-glyph alphabet gives a ~7.9e17
    /// keyspace - far longer than the 4-char join code (which only needs to be
    /// unique among active rooms), so a public link resists enumeration (AC-03).
    /// </summary>
    public const int SlugLength = 12;

    /// <summary>
    /// Mints one fresh unguessable slug: <see cref="SlugLength"/> characters, each
    /// picked with an unbiased cryptographic RNG from <see cref="Alphabet"/>. Never
    /// sequential, never time-derived.
    /// </summary>
    public static string Generate()
    {
        Span<char> slug = stackalloc char[SlugLength];
        for (var i = 0; i < SlugLength; i++)
        {
            slug[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(slug);
    }
}
