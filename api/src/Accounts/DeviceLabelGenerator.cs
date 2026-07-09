// ----------------------------------------------------------------------------
//  DeviceLabelGenerator - mints the short, random, NON-identifying label attached to a
//  linked device at redeem time (accounts-identity/09, issue #229, AC-04).
//
//  WHY A RANDOM LABEL (not a device fingerprint): the Account page's linked-devices
//  list must give the parent enough to make revocation an actionable choice - but
//  NOTHING device-identifying (no IP, no user agent, no OS, no serial). A random
//  adjective-noun tag ("Brave Otter") is memorable enough to tell two linked devices
//  apart while carrying zero information about the device or the kid (README section 6,
//  minimal data on minors). The words are deliberately playful and family-safe,
//  matching the Guardian / stone-tablet voice.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Generates a two-word, non-identifying device label (accounts-identity/09, AC-04):
/// a random adjective + a random noun, CSPRNG-picked, family-safe, carrying no PII and
/// no device fingerprint. A pure static helper - the label is a display convenience, not
/// a stored secret.
/// </summary>
public static class DeviceLabelGenerator
{
    // Deliberately small, playful, and family-safe word lists. They exist only to make
    // one linked tile distinguishable from another; collisions are harmless (the
    // revocation handle is the opaque DeviceTokenId, never the label).
    private static readonly string[] Adjectives =
    [
        "Brave", "Sunny", "Cozy", "Jolly", "Swift", "Gentle", "Merry", "Snug",
        "Bouncy", "Cheery", "Plucky", "Breezy", "Bright", "Quirky", "Nimble", "Zippy",
    ];

    private static readonly string[] Nouns =
    [
        "Otter", "Badger", "Sparrow", "Pebble", "Acorn", "Lantern", "Maple", "Willow",
        "Comet", "Meadow", "Pinecone", "Compass", "Kettle", "Marble", "Puffin", "Thistle",
    ];

    /// <summary>
    /// Mints a fresh "Adjective Noun" label using the CSPRNG (no PII, no fingerprint).
    /// </summary>
    public static string Next()
    {
        var adjective = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        var noun = Nouns[RandomNumberGenerator.GetInt32(Nouns.Length)];
        return $"{adjective} {noun}";
    }
}
