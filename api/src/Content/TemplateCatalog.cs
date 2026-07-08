// ----------------------------------------------------------------------------
//  TemplateCatalog - the MINIMAL server-side template catalog (group-play/01).
//
//  ============================ KEEP IN SYNC BY HAND ==========================
//  This catalog MIRRORS web/src/content/seedLibrary.ts - one entry per template,
//  matched by Id. It is a WIRE-CONTRACT-STYLE mirror (same discipline as the
//  PlayerDto / RoomStateDto DTOs in GameHub.cs): when a template is added,
//  removed, renamed, or its familySafe tag / blank count / word-bank presence
//  changes in seedLibrary.ts, THIS file MUST be updated to match by hand. There
//  is no codegen and no shared source - the two drift silently if you forget.
//  ============================================================================
//
//  Why the server holds ONLY { Id, FamilySafe, BlankCount, HasWordBank } and NOT the prose:
//  QuibbleStone's charter is "one engine, many thin modes" (README section 4) -
//  the template PROSE / prompts / body / titles stay CLIENT-SIDE in seedLibrary,
//  and each client resolves a template's full content from that bundled library
//  BY ID. Copying the prose into C# would duplicate content and risk forking the
//  engine, so the server deliberately carries only the tiny facts it needs to
//  make an authoritative, safety-aware selection:
//    - Id         : the stable key the client resolves full content from.
//    - FamilySafe : the ONLY signal the family-safe gate acts on (child-safety/02,
//                   AC-04). The catalog now carries BOTH a family-safe set and a
//                   non-family-safe (teen-plus) tier, so the gate meaningfully
//                   narrows a family-safe round to the FamilySafe:true subset and
//                   only opens the rest when the host turns the toggle off.
//    - BlankCount : carried NOW (group-play/01) so group-play/02's index-based
//                   blank distribution / attribution does not have to reshape this
//                   catalog later. group-play/01 itself does not read it.
//    - HasWordBank: group-play/05 - the one signal the Word Bank mode's per-mode
//                   eligibility gate reads (mirrors the web's offerWordBankTemplates),
//                   so a bank-less template is never picked for a Word Bank round.
//
//  This is PURE DATA (like seedLibrary), stateless, and registered as a singleton
//  in DI (Program.cs) so every transient GameHub instance shares the one catalog.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Content;

/// <summary>
/// One template as the SERVER knows it (group-play/01). Deliberately minimal:
/// the id the client resolves full content from, the family-safe flag the gate
/// acts on (child-safety/02), and the blank count group-play/02 will use for
/// index-based distribution. Mirrors one entry of web/src/content/seedLibrary.ts
/// by <see cref="Id"/> - see the file header's KEEP IN SYNC note.
/// </summary>
/// <param name="Id">Stable template id - the key the client resolves full prose/body from seedLibrary.</param>
/// <param name="FamilySafe">True when the template is tagged family-safe (seedLibrary tags.familySafe). The one signal the family-safe gate reads (AC-04).</param>
/// <param name="BlankCount">Number of blanks in the template - carried for group-play/02's distribution; unused by group-play/01.</param>
/// <param name="HasWordBank">True when the template carries a curated word bank (seedLibrary `wordBank`). group-play/05: the ONLY signal the Word Bank mode's per-mode eligibility gate reads, mirroring the web's offerWordBankTemplates rule - a bank-less template is never offered for Word Bank, so the mode can never draw one and render an empty tap list. Defaults to false, so a template not tagged here is simply never a Word Bank pick.</param>
public sealed record TemplateCatalogEntry(string Id, bool FamilySafe, int BlankCount, bool HasWordBank = false);

/// <summary>
/// The minimal server-side template catalog: one <see cref="TemplateCatalogEntry"/>
/// per web seed template, kept in sync with web/src/content/seedLibrary.ts BY HAND
/// (see the file header). Registered as a singleton; the hub reads
/// <see cref="Entries"/> to select a round's template.
/// </summary>
public sealed class TemplateCatalog
{
    // Mirrors web/src/content/seedLibrary.ts (id, tags.familySafe, blank count).
    // The library ships two tiers: a family-safe set (FamilySafe: true) and a
    // non-family-safe "toggle off" set (FamilySafe: false, ageRating teen-plus in
    // seedLibrary). The FULL stories run 8-10 blanks (long on purpose, so
    // round-robin distribution gives every player in a full room multiple blanks;
    // see the seedLibrary.ts header). The QUICK stories (story-selection/01) run
    // 4-6 blanks - the LengthContentSelector classifies quick (<= 6) vs full
    // (>= 7) from these counts, so keeping each BlankCount exact here is what
    // keeps the server length filter in lockstep with the web one. Non-family-safe
    // templates carry FamilySafe: false so the family-safe gate filters them out
    // of a family-safe round and offers them only when the toggle is off (AC-04).
    private static readonly IReadOnlyList<TemplateCatalogEntry> Catalog =
    [
        // Full stories (9-10 blanks).
        new TemplateCatalogEntry("wobbly-wizard", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("space-llama", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("road-trip-disaster", FamilySafe: true, BlankCount: 10, HasWordBank: true),
        new TemplateCatalogEntry("school-of-noodles", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("monster-under-bed", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("food-truck-frenzy", FamilySafe: true, BlankCount: 9, HasWordBank: true),
        new TemplateCatalogEntry("backyard-safari", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("pirate-puddle", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("robot-recital", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("dragon-sock-thief", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("birthday-balloon-mayhem", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("snowman-summer-job", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("grandmas-secret-recipe", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("how-to-be-famous", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("the-new-neighbor", FamilySafe: true, BlankCount: 10),
        // Quick stories (4-6 blanks) - story-selection/01.
        new TemplateCatalogEntry("sneezy-dinosaur", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("invisible-sandwich", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("grumpy-goldfish", FamilySafe: true, BlankCount: 4),
        new TemplateCatalogEntry("dancing-broom", FamilySafe: true, BlankCount: 6),
        // Wave 2 family-safe stories (mixed quick + full) - see seedLibrary.ts.
        new TemplateCatalogEntry("fa-penguin-heat-wave", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fa-squirrel-acorn-heist", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("fa-polite-bear-picnic", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fa-chameleon-cant-hide", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("fa-dog-thinks-cat", FamilySafe: true, BlankCount: 6),
        new TemplateCatalogEntry("fs-alien-exchange-student", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fs-vacuum-rebellion", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("fs-lost-spaceship-gps", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fs-lunchtime-time-machine", FamilySafe: true, BlankCount: 6),
        new TemplateCatalogEntry("fs-ufo-county-fair", FamilySafe: true, BlankCount: 6),
        new TemplateCatalogEntry("fc-traveling-sourdough", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fc-school-bake-off-chaos", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("fc-pizza-wrong-planet", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fc-veggie-protest", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("fc-wild-smoothie", FamilySafe: true, BlankCount: 6),
        new TemplateCatalogEntry("fl-messiest-bedroom-cleanup", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fl-goose-soccer-showdown", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("fl-tooth-fairy-negotiation", FamilySafe: true, BlankCount: 8),
        new TemplateCatalogEntry("fl-picture-day-disaster", FamilySafe: true, BlankCount: 9),
        new TemplateCatalogEntry("fl-snow-day-master-plan", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("fl-lemonade-empire", FamilySafe: true, BlankCount: 6),
        // Non-family-safe "toggle off" stories (ageRating teen-plus in seedLibrary).
        // FamilySafe: false so the family-safe gate filters them OUT of a
        // family-safe round and offers them ONLY when the toggle is off (AC-04).
        new TemplateCatalogEntry("ad-worst-first-date", FamilySafe: false, BlankCount: 10),
        new TemplateCatalogEntry("ad-dating-profile", FamilySafe: false, BlankCount: 10),
        new TemplateCatalogEntry("ad-meet-the-parents", FamilySafe: false, BlankCount: 10),
        new TemplateCatalogEntry("ad-just-friends-text", FamilySafe: false, BlankCount: 6),
        new TemplateCatalogEntry("ad-group-chat-ex", FamilySafe: false, BlankCount: 6),
        new TemplateCatalogEntry("ao-monday-all-hands", FamilySafe: false, BlankCount: 9),
        new TemplateCatalogEntry("ao-holiday-party-chaos", FamilySafe: false, BlankCount: 10),
        new TemplateCatalogEntry("ao-performance-review", FamilySafe: false, BlankCount: 8),
        new TemplateCatalogEntry("ao-wfh-mute-fail", FamilySafe: false, BlankCount: 5),
        new TemplateCatalogEntry("ao-breakroom-fridge-note", FamilySafe: false, BlankCount: 6),
        new TemplateCatalogEntry("ap-what-happened-last-night", FamilySafe: false, BlankCount: 9),
        new TemplateCatalogEntry("ap-vegas-blackout", FamilySafe: false, BlankCount: 10),
        new TemplateCatalogEntry("ap-new-years-resolution", FamilySafe: false, BlankCount: 5),
        new TemplateCatalogEntry("ap-karaoke-showdown", FamilySafe: false, BlankCount: 6),
    ];

    /// <summary>The full catalog (host first ordering is irrelevant - selection is random).</summary>
    public IReadOnlyList<TemplateCatalogEntry> Entries => Catalog;
}
